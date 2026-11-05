using Application.DTOs.DayConflicts;
using Application.Exceptions;
using Application.Interfaces.Services;

namespace Application.UseCases.DayConflicts.GetDayConflicts;

public sealed record GetDayConflictsQuery(Guid AthleteId);

/// <summary>
/// Detecta si la rutina de hoy del atleta carga zonas anatómicas donde reportó dolor
/// en el check-in de hoy (#156 · KAN-451).
/// </summary>
public sealed class GetDayConflictsQueryHandler(
    ITrainingPlanServiceClient trainingClient,
    ISeguimientoClient seguimientoClient)
{
    public const string MissingCheckInMessage = "Todavía no hizo el check-in";

    public async Task<DayConflictsResponseDto> HandleAsync(
        GetDayConflictsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.AthleteId == Guid.Empty)
            throw new NotFoundException("El atleta no existe o no pertenece al preparador físico autenticado.");

        // 1. Obtener la rutina de hoy en Entrenamiento (si devuelve 404, el atleta no existe o es de otro PF).
        var routine = await trainingClient.GetTodayRoutineAsync(query.AthleteId, cancellationToken)
            ?? throw new NotFoundException("El atleta no existe o no pertenece al preparador físico autenticado.");

        // 2. Obtener el check-in de hoy en Seguimiento (si devuelve 404, todavía no hizo el check-in hoy).
        var checkIn = await seguimientoClient.GetTodayCheckInAsync(query.AthleteId, cancellationToken);
        if (checkIn is null)
        {
            return new DayConflictsResponseDto(
                HasConflicts: false,
                PainZones: null,
                Conflicts: [],
                Message: MissingCheckInMessage);
        }

        // 3. Si no reportó zonas de dolor o no tiene ejercicios hoy, no hay conflictos.
        var exercises = routine.Exercises ?? [];
        if (string.IsNullOrWhiteSpace(checkIn.PainZones) || exercises.Count == 0)
        {
            return new DayConflictsResponseDto(
                HasConflicts: false,
                PainZones: checkIn.PainZones,
                Conflicts: []);
        }

        var exerciseIds = exercises
            .Select(e => e.ExerciseId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (exerciseIds.Count == 0)
        {
            return new DayConflictsResponseDto(
                HasConflicts: false,
                PainZones: checkIn.PainZones,
                Conflicts: []);
        }

        // 4. Cruzar painZones con los ejercicios de la rutina en Entrenamiento.
        var restrictionResults = await trainingClient.CheckExerciseRestrictionsAsync(
            checkIn.PainZones,
            exerciseIds,
            cancellationToken);

        var namesById = exercises
            .Where(e => e.ExerciseId != Guid.Empty)
            .GroupBy(e => e.ExerciseId)
            .ToDictionary(g => g.Key, g => g.First().Name);

        var conflicts = restrictionResults
            .Where(r => r.Restricted)
            .Select(r => new DayConflictItemDto(
                ExerciseId: r.ExerciseId,
                Name: namesById.GetValueOrDefault(r.ExerciseId, string.Empty),
                Zone: r.Zone ?? string.Empty,
                PainLevel: r.PainLevel ?? 0,
                Reason: r.Reason ?? string.Empty,
                Substitutes: r.Substitutes ?? []))
            .ToList();

        return new DayConflictsResponseDto(
            HasConflicts: conflicts.Count > 0,
            PainZones: checkIn.PainZones,
            Conflicts: conflicts);
    }
}
