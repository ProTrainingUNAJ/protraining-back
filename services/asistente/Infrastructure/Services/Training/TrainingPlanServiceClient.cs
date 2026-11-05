using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.DTOs.DayConflicts;
using Application.DTOs.PlanDrafts;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Training;

/// <summary>
/// Cliente HTTP hacia el microservicio de Entrenamiento para crear una planificación
/// y asignarla al aprobar un borrador de IA (#116 · KAN-411) y para consultar la rutina de hoy
/// y sus restricciones biomecánicas por dolor (#156 · KAN-451).
/// Reenvía el token del preparador físico mediante ForwardAuthorizationHandler.
/// </summary>
public sealed class TrainingPlanServiceClient(
    HttpClient httpClient,
    ILogger<TrainingPlanServiceClient> logger) : ITrainingPlanServiceClient
{
    public const string TrainingPlansPath = "api/v1/training-plans";
    public const string AssignmentsPath = "api/v1/training-plans/assignments";
    public const string RestrictionsCheckPath = "api/v1/exercises/restrictions/check";
    public const string CreatePlanErrorMessage = "No se pudo crear la planificación, probá de nuevo";
    public const string TrainingUnavailableMessage = "El microservicio de Entrenamiento no se encuentra disponible.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private sealed record CreatedTrainingPlanRemoteDto(
        [property: JsonPropertyName("id")] Guid Id
    );

    private sealed record AssignedTrainingPlanRemoteDto(
        [property: JsonPropertyName("assignmentId")] Guid? AssignmentId = null,
        [property: JsonPropertyName("id")] Guid? Id = null
    );

    public async Task<Guid> CreatePlanAsync(
        SaveTrainingPlanRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(TrainingPlansPath, request, JsonOptions, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Error de comunicación con Entrenamiento al crear la planificación: {ErrorType}.",
                ex.GetType().Name);
            throw new TrainingServiceUnavailableException(CreatePlanErrorMessage, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Entrenamiento respondió {StatusCode} al intentar crear la planificación.",
                    (int)response.StatusCode);
                throw new TrainingServiceUnavailableException(CreatePlanErrorMessage);
            }

            try
            {
                var created = await response.Content.ReadFromJsonAsync<CreatedTrainingPlanRemoteDto>(JsonOptions, cancellationToken);
                if (created is null || created.Id == Guid.Empty)
                {
                    logger.LogError("Entrenamiento respondió sin un identificador de planificación válido.");
                    throw new TrainingServiceUnavailableException(CreatePlanErrorMessage);
                }

                return created.Id;
            }
            catch (TrainingServiceUnavailableException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    "Error al interpretar la respuesta de Entrenamiento al crear la planificación: {ErrorType}.",
                    ex.GetType().Name);
                throw new TrainingServiceUnavailableException(CreatePlanErrorMessage, ex);
            }
        }
    }

    public async Task<AssignTrainingPlanResult> TryAssignPlanAsync(
        AssignTrainingPlanRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(AssignmentsPath, request, JsonOptions, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "No se pudo conectar con Entrenamiento para asignar la planificación {TrainingPlanId}: {ErrorType}.",
                request.TrainingPlanId,
                ex.GetType().Name);
            return new AssignTrainingPlanResult(Assigned: false, AssignmentId: null);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Entrenamiento respondió {StatusCode} al asignar la planificación {TrainingPlanId}.",
                    (int)response.StatusCode,
                    request.TrainingPlanId);
                return new AssignTrainingPlanResult(Assigned: false, AssignmentId: null);
            }

            try
            {
                var assigned = await response.Content.ReadFromJsonAsync<AssignedTrainingPlanRemoteDto>(JsonOptions, cancellationToken);
                var assignmentId = assigned?.AssignmentId ?? assigned?.Id;
                return new AssignTrainingPlanResult(
                    Assigned: true,
                    AssignmentId: assignmentId == Guid.Empty ? null : assignmentId);
            }
            catch (Exception)
            {
                return new AssignTrainingPlanResult(Assigned: true, AssignmentId: null);
            }
        }
    }

    public async Task<TodayRoutineRemoteDto?> GetTodayRoutineAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        if (httpClient.BaseAddress is null)
            throw new TrainingServiceUnavailableException(TrainingUnavailableMessage);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync($"api/v1/athletes/{athleteId}/today-routine", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Error de comunicación con Entrenamiento al consultar la rutina de hoy del atleta {AthleteId}: {ErrorType}.",
                athleteId,
                ex.GetType().Name);
            throw new TrainingServiceUnavailableException(TrainingUnavailableMessage, ex);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    try
                    {
                        return await response.Content.ReadFromJsonAsync<TodayRoutineRemoteDto>(JsonOptions, cancellationToken)
                            ?? throw new TrainingServiceUnavailableException(TrainingUnavailableMessage);
                    }
                    catch (TrainingServiceUnavailableException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            "Error al interpretar la rutina de hoy del atleta {AthleteId} desde Entrenamiento: {ErrorType}.",
                            athleteId,
                            ex.GetType().Name);
                        throw new TrainingServiceUnavailableException(TrainingUnavailableMessage, ex);
                    }

                case HttpStatusCode.NotFound:
                    return null;

                case HttpStatusCode.Unauthorized:
                    throw new UnauthorizedAccessException();

                default:
                    logger.LogError(
                        "Entrenamiento respondió {StatusCode} al consultar la rutina de hoy del atleta {AthleteId}.",
                        (int)response.StatusCode,
                        athleteId);
                    throw new TrainingServiceUnavailableException(TrainingUnavailableMessage);
            }
        }
    }

    public async Task<IReadOnlyList<ExerciseRestrictionCheckResultRemoteDto>> CheckExerciseRestrictionsAsync(
        string painZones,
        IReadOnlyList<Guid> exerciseIds,
        CancellationToken cancellationToken = default)
    {
        if (exerciseIds.Count == 0)
            return [];

        if (httpClient.BaseAddress is null)
            throw new TrainingServiceUnavailableException(TrainingUnavailableMessage);

        var request = new CheckExerciseRestrictionsRequestDto(painZones, exerciseIds);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(RestrictionsCheckPath, request, JsonOptions, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Error de comunicación con Entrenamiento al chequear restricciones de ejercicios: {ErrorType}.",
                ex.GetType().Name);
            throw new TrainingServiceUnavailableException(TrainingUnavailableMessage, ex);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    try
                    {
                        var results = await response.Content.ReadFromJsonAsync<List<ExerciseRestrictionCheckResultRemoteDto>>(JsonOptions, cancellationToken);
                        return results ?? [];
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            "Error al interpretar la respuesta de restricciones de Entrenamiento: {ErrorType}.",
                            ex.GetType().Name);
                        throw new TrainingServiceUnavailableException(TrainingUnavailableMessage, ex);
                    }

                case HttpStatusCode.Unauthorized:
                    throw new UnauthorizedAccessException();

                default:
                    logger.LogError(
                        "Entrenamiento respondió {StatusCode} al chequear restricciones de ejercicios.",
                        (int)response.StatusCode);
                    throw new TrainingServiceUnavailableException(TrainingUnavailableMessage);
            }
        }
    }
}
