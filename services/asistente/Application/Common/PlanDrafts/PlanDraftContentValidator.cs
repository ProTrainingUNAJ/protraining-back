using Application.DTOs.PlanDrafts;
using Application.Interfaces.Services;
using Domain.Exceptions;

namespace Application.Common.PlanDrafts;

/// <summary>
/// Valida el esquema, rangos deportivos (series, repeticiones, porcentajes) y existencia real
/// de los ejercicios en el catálogo oficial para un borrador de planificación (KAN-383 / KAN-410).
/// </summary>
public static class PlanDraftContentValidator
{
    public const int MinWeeks = 1;
    public const int MaxWeeks = 8;
    public const int MinSets = 1;
    public const int MaxSets = 10;
    public const int MinReps = 1;
    public const int MaxReps = 30;
    public const decimal MinPercentage = 0.0m;
    public const decimal MaxPercentage = 100.0m;

    public static async Task<IReadOnlyDictionary<Guid, string>> ValidateAsync(
        PlanDraftContentDto content,
        IExerciseCatalogService catalogService,
        CancellationToken cancellationToken = default)
    {
        if (content is null)
            throw new DomainException("El contenido del borrador es obligatorio.");

        if (string.IsNullOrWhiteSpace(content.Name) || content.Name.Trim().Length < 3)
            throw new DomainException("El nombre del borrador es obligatorio y debe tener al menos 3 caracteres.");

        if (string.IsNullOrWhiteSpace(content.Objective) || content.Objective.Trim().Length < 3)
            throw new DomainException("El objetivo del borrador es obligatorio y debe tener al menos 3 caracteres.");

        if (string.IsNullOrWhiteSpace(content.JustificationSummary) || content.JustificationSummary.Trim().Length < 5)
            throw new DomainException("El resumen de justificación es obligatorio y debe tener al menos 5 caracteres.");

        if (content.Weeks is null || content.Weeks.Count is < MinWeeks or > MaxWeeks)
            throw new DomainException($"El borrador debe contener entre {MinWeeks} y {MaxWeeks} semanas.");

        var totalSessionsWithExercises = 0;
        var exerciseIdsToVerify = new HashSet<Guid>();

        foreach (var week in content.Weeks)
        {
            if (week.WeekNumber is < MinWeeks or > MaxWeeks)
                throw new DomainException($"El número de semana debe estar entre {MinWeeks} y {MaxWeeks} (se recibió {week.WeekNumber}).");

            if (week.Sessions is null || week.Sessions.Count == 0)
                continue;

            foreach (var session in week.Sessions)
            {
                if (string.IsNullOrWhiteSpace(session.Name))
                    throw new DomainException($"Toda sesión en la semana {week.WeekNumber} debe tener un nombre.");

                if (session.Order < 1)
                    throw new DomainException($"El orden de la sesión '{session.Name}' en la semana {week.WeekNumber} debe ser mayor o igual a 1.");

                if (!Enum.IsDefined(typeof(DayOfWeek), session.DayOfWeek))
                    throw new DomainException($"El día de la semana de la sesión '{session.Name}' no es válido.");

                if (session.Exercises is null || session.Exercises.Count == 0)
                    throw new DomainException($"La sesión '{session.Name}' (semana {week.WeekNumber}) debe contener al menos un ejercicio.");

                totalSessionsWithExercises++;

                foreach (var exercise in session.Exercises)
                {
                    if (exercise.ExerciseId == Guid.Empty)
                        throw new DomainException($"Todos los ejercicios de la sesión '{session.Name}' (semana {week.WeekNumber}) deben tener un exerciseId válido.");

                    if (exercise.Order < 1)
                        throw new DomainException($"El orden del ejercicio en la sesión '{session.Name}' debe ser mayor o igual a 1.");

                    if (exercise.Sets is < MinSets or > MaxSets)
                        throw new DomainException($"Las series del ejercicio '{exercise.ExerciseId}' deben estar entre {MinSets} y {MaxSets} (se recibió {exercise.Sets}).");

                    if (exercise.Reps is < MinReps or > MaxReps)
                        throw new DomainException($"Las repeticiones del ejercicio '{exercise.ExerciseId}' deben estar entre {MinReps} y {MaxReps} (se recibió {exercise.Reps}).");

                    if (exercise.TargetPercentage.HasValue &&
                        (exercise.TargetPercentage.Value < MinPercentage || exercise.TargetPercentage.Value > MaxPercentage))
                    {
                        throw new DomainException($"El porcentaje objetivo del ejercicio '{exercise.ExerciseId}' debe estar entre {MinPercentage} y {MaxPercentage} (se recibió {exercise.TargetPercentage.Value}).");
                    }

                    exerciseIdsToVerify.Add(exercise.ExerciseId);
                }
            }
        }

        if (totalSessionsWithExercises == 0)
            throw new DomainException("El borrador debe tener al menos una sesión con al menos un ejercicio.");

        var exerciseNames = new Dictionary<Guid, string>();
        foreach (var exerciseId in exerciseIdsToVerify)
        {
            var catalogItem = await catalogService.FindByIdAsync(exerciseId, cancellationToken);
            if (catalogItem is null)
                throw new DomainException($"El ejercicio con identificador '{exerciseId}' no existe en el catálogo oficial.");

            exerciseNames[exerciseId] = catalogItem.Name;
        }

        return exerciseNames;
    }
}
