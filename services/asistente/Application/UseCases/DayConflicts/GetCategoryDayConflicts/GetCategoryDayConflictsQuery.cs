using System.Collections.Concurrent;
using Application.DTOs.DayConflicts;
using Application.Exceptions;
using Application.Interfaces.Services;

namespace Application.UseCases.DayConflicts.GetCategoryDayConflicts;

public sealed record GetCategoryDayConflictsQuery(Guid CategoryId);

/// <summary>
/// Evalúa en una sola llamada por categoría qué atletas presentan conflictos entre el dolor
/// reportado en su check-in de hoy y los ejercicios de su rutina de hoy (KAN-574).
/// Solo consulta Entrenamiento para atletas que reportaron zonas de dolor, memoiza chequeos
/// idénticos de restricciones dentro de la misma petición y omite MCP porque el semáforo
/// únicamente necesita saber si existe al menos un conflicto.
/// </summary>
public sealed class GetCategoryDayConflictsQueryHandler(
    ITrainingPlanServiceClient trainingClient,
    ISeguimientoClient seguimientoClient)
{
    private const int MaxConcurrency = 5;

    public async Task<CategoryDayConflictsResponseDto> HandleAsync(
        GetCategoryDayConflictsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.CategoryId == Guid.Empty)
            throw new NotFoundException("La categoría no existe o no pertenece al preparador físico autenticado.");

        // 1. Obtener el resumen de hoy de toda la categoría en una sola llamada a Seguimiento
        //    (valida pertenencia de la categoría al PF y devuelve 404 si no existe o es de otro PF).
        var categoryAthletes = await seguimientoClient.GetCategoryTrafficLightTodayAsync(
            query.CategoryId,
            cancellationToken);

        var uniqueAthletes = categoryAthletes
            .Where(a => a.AthleteId != Guid.Empty)
            .GroupBy(a => a.AthleteId)
            .Select(g => g.First())
            .ToList();

        if (uniqueAthletes.Count == 0)
        {
            return new CategoryDayConflictsResponseDto(query.CategoryId, []);
        }

        using var semaphore = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var restrictionCache = new ConcurrentDictionary<string, Lazy<Task<bool>>>(StringComparer.Ordinal);

        var tasks = uniqueAthletes.Select(athlete =>
            EvaluateAthleteAsync(athlete, semaphore, restrictionCache, cancellationToken));

        var results = await Task.WhenAll(tasks);

        return new CategoryDayConflictsResponseDto(query.CategoryId, results);
    }

    private async Task<CategoryAthleteDayConflictDto> EvaluateAthleteAsync(
        CategoryTrafficLightAthleteRemoteDto athlete,
        SemaphoreSlim semaphore,
        ConcurrentDictionary<string, Lazy<Task<bool>>> restrictionCache,
        CancellationToken cancellationToken)
    {
        var painZones = athlete.Indicators?.PainZones;
        if (!athlete.HasReportedToday || string.IsNullOrWhiteSpace(painZones))
        {
            return new CategoryAthleteDayConflictDto(athlete.AthleteId, HasConflicts: false);
        }

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var routine = await trainingClient.GetTodayRoutineAsync(athlete.AthleteId, cancellationToken);
            var exercises = routine?.Exercises;
            if (exercises is null || exercises.Count == 0)
            {
                return new CategoryAthleteDayConflictDto(athlete.AthleteId, HasConflicts: false);
            }

            var exerciseIds = exercises
                .Select(e => e.ExerciseId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            if (exerciseIds.Count == 0)
            {
                return new CategoryAthleteDayConflictDto(athlete.AthleteId, HasConflicts: false);
            }

            var normalizedPainZones = painZones.Trim();
            var cacheKey = BuildRestrictionCacheKey(normalizedPainZones, exerciseIds);

            var hasConflicts = await restrictionCache.GetOrAdd(
                cacheKey,
                _ => new Lazy<Task<bool>>(
                    () => CheckHasConflictsAsync(normalizedPainZones, exerciseIds, cancellationToken),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;

            return new CategoryAthleteDayConflictDto(athlete.AthleteId, hasConflicts);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<bool> CheckHasConflictsAsync(
        string painZones,
        IReadOnlyList<Guid> exerciseIds,
        CancellationToken cancellationToken)
    {
        var restrictionResults = await trainingClient.CheckExerciseRestrictionsAsync(
            painZones,
            exerciseIds,
            cancellationToken);

        return restrictionResults.Any(r => r.Restricted);
    }

    private static string BuildRestrictionCacheKey(string painZones, IReadOnlyList<Guid> sortedExerciseIds) =>
        $"{painZones.ToLowerInvariant()}|{string.Join(',', sortedExerciseIds)}";
}
