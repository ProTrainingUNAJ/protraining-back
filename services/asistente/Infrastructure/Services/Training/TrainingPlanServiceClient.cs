using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.DTOs.PlanDrafts;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Training;

/// <summary>
/// Cliente HTTP hacia el microservicio de Entrenamiento para crear una planificación
/// y asignarla al aprobar un borrador de IA (#116 · KAN-411).
/// Reenvía el token del preparador físico mediante ForwardAuthorizationHandler.
/// </summary>
public sealed class TrainingPlanServiceClient(
    HttpClient httpClient,
    ILogger<TrainingPlanServiceClient> logger) : ITrainingPlanServiceClient
{
    public const string TrainingPlansPath = "api/v1/training-plans";
    public const string AssignmentsPath = "api/v1/training-plans/assignments";
    public const string CreatePlanErrorMessage = "No se pudo crear la planificación, probá de nuevo";

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
}
