namespace Application.Interfaces.Services;

// Consultas y envíos a Entrenamiento por HTTP, con el token de la petición.
// La resiliencia (reintentos, timeout y circuit breaker) la aporta el pipeline global
// de Polly configurado en Program.cs; los errores de transporte se traducen en
// EntrenamientoUnavailableException para responder 503 al PF.
public interface IEntrenamientoClient
{
    // Ejercicio de Entrenamiento con nombre exacto al indicado; null si no existe.
    // Se usa una sola vez por test (la primera confirmación): después el GUID queda
    // cacheado en CatalogTest.ExerciseId.
    Task<Guid?> FindExerciseIdByNameAsync(string exerciseName, CancellationToken cancellationToken = default);

    // Registra el 1RM del atleta con el origen indicado ("TestedInSession" = testeado).
    // Lanza EntrenamientoUnavailableException si Entrenamiento no responde.
    Task SaveRecordAsync(
        Guid athleteId,
        Guid exerciseId,
        decimal weightOneRepMaxKg,
        string origin,
        CancellationToken cancellationToken = default);
}
