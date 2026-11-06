using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Polly;

namespace Infrastructure.Services.Entrenamiento;

// Cliente HTTP hacia Entrenamiento. El token de la petición lo agrega
// ForwardAuthorizationHandler (en la API) y la resiliencia (reintentos con backoff,
// timeout por intento y circuit breaker) viene del pipeline global de Polly aplicado
// en Program.cs con AddProTrainingResilience.
public sealed class EntrenamientoClient(HttpClient httpClient, ILogger<EntrenamientoClient> logger) : IEntrenamientoClient
{
    public const string ExercisesPath = "api/v1/exercises";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // El contrato de Entrenamiento usa camelCase (exerciseId, weightOneRepMaxKg, origin).
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<Guid?> FindExerciseIdByNameAsync(string exerciseName, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            // Búsqueda por similitud (pg_trgm) en Entrenamiento: el match exacto por nombre
            // se hace acá, porque el candidato correcto puede no ser el primero del ranking.
            var url = $"{ExercisesPath}?search={Uri.EscapeDataString(exerciseName)}&pageSize=50";
            response = await httpClient.GetAsync(url, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        // Mismo criterio que StructureClient: las rejecciones del pipeline de
        // resiliencia (breaker abierto o timeout) deben degradar a EntrenamientoUnavailable
        // (503), nunca escapar como 500.
        catch (Exception exception) when (exception is HttpRequestException
                                          or TaskCanceledException
                                          or ExecutionRejectedException)
        {
            throw new EntrenamientoUnavailableException(exception);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var page = await response.Content
                        .ReadFromJsonAsync<PaginatedExercises>(JsonOptions, cancellationToken);
                    var match = page?.Items?.FirstOrDefault(item =>
                        string.Equals(item.Name?.Trim(), exerciseName.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (match is null)
                    {
                        logger.LogWarning(
                            "Entrenamiento no tiene un ejercicio con nombre exacto \"{ExerciseName}\".",
                            exerciseName);
                        return null;
                    }

                    return match.Id;

                case HttpStatusCode.Unauthorized:
                    throw new UnauthorizedAccessException();

                case HttpStatusCode.Forbidden:
                    throw new ForbiddenException("La cuenta del token no está autorizada como preparador físico.");

                default:
                    logger.LogError("Entrenamiento respondió {Status} al buscar el ejercicio \"{ExerciseName}\".",
                        (int)response.StatusCode, exerciseName);
                    throw new EntrenamientoUnavailableException();
            }
        }
    }

    public async Task SaveRecordAsync(
        Guid athleteId,
        Guid exerciseId,
        decimal weightOneRepMaxKg,
        string origin,
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            var request = new SaveRecordRequest(exerciseId, weightOneRepMaxKg, origin);
            response = await httpClient.PostAsJsonAsync(
                $"api/v1/athletes/{athleteId}/records", request, JsonOptions, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                          or TaskCanceledException
                                          or ExecutionRejectedException)
        {
            throw new EntrenamientoUnavailableException(exception);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                case HttpStatusCode.Created:
                    return;

                case HttpStatusCode.Unauthorized:
                    throw new UnauthorizedAccessException();

                case HttpStatusCode.Forbidden:
                    throw new ForbiddenException("La cuenta del token no está autorizada como preparador físico.");

                // Entrenamiento valida el récord (peso fuera de rango u origen inválido):
                // el mensaje del ProblemDetails lo rechaza su middleware, acá solo se
                // propaga el rechazo como 400.
                case HttpStatusCode.BadRequest:
                    logger.LogWarning("Entrenamiento rechazó el récord del atleta {AthleteId}: {Reason}.",
                        athleteId, await response.Content.ReadAsStringAsync(cancellationToken));
                    throw new Domain.Exceptions.DomainException(
                        "Entrenamiento rechazó el récord: revisá el valor y el origen.");

                default:
                    logger.LogError("Entrenamiento respondió {Status} al guardar el récord del atleta {AthleteId}.",
                        (int)response.StatusCode, athleteId);
                    throw new EntrenamientoUnavailableException();
            }
        }
    }

    // Contrato de Entrenamiento: POST /api/v1/athletes/{athleteId}/records (saveRecord).
    private sealed record SaveRecordRequest(Guid ExerciseId, decimal WeightOneRepMaxKg, string Origin);

    // Contrato de Entrenamiento: GET /api/v1/exercises devuelve PaginatedExerciseResponse
    // (items/page/pageSize/totalCount/totalPages). Solo se leen los campos necesarios.
    private sealed class PaginatedExercises
    {
        [JsonPropertyName("items")]
        public List<ExerciseItem>? Items { get; set; }
    }

    private sealed class ExerciseItem
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
