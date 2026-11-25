using System.Globalization;
using Application.DTOs.PlanDrafts;
using Domain.Common;

namespace Infrastructure.Services.Ai.Rules;

/// <summary>
/// Proyecta las semanas de un borrador armado por reglas con progresión semanal de +2,5 %RM hasta el tope
/// del objetivo, reducción en semanas con partido y descarga en la última semana cuando el plan tiene 4+ semanas (KAN-605).
/// </summary>
public static class RuleBasedPlanProgression
{
    private static readonly CultureInfo EsArCulture = CultureInfo.GetCultureInfo("es-AR");

    public const decimal WeeklyIncrementPercentage = 2.5m;
    public const decimal ReducedWeekPercentagePenalty = 10.0m;
    public const decimal MinTargetPercentage = 40.0m;

    public const string WarmUpNote =
        "Entrada en calor (10 min): movilidad articular de los músculos del día y series de aproximación al 40-60% antes del primer ejercicio pesado.";

    public static List<PlanDraftWeekDto> GenerateWeeks(
        IReadOnlyList<RuleSessionBlueprint> sessionBlueprints,
        IReadOnlyList<DayOfWeek> trainingDays,
        RuleGoalProfile profile,
        int totalWeeks,
        DateOnly startDate,
        IReadOnlyList<DateOnly>? matchDates = null)
    {
        if (sessionBlueprints.Count == 0 || trainingDays.Count == 0)
            return [];

        totalWeeks = Math.Clamp(totalWeeks, 1, 8);
        var matchSet = matchDates is { Count: > 0 }
            ? new HashSet<DateOnly>(matchDates)
            : null;

        var sessionCount = Math.Min(sessionBlueprints.Count, trainingDays.Count);
        var resultWeeks = new List<PlanDraftWeekDto>(totalWeeks);

        for (var weekNumber = 1; weekNumber <= totalWeeks; weekNumber++)
        {
            var weekStart = startDate.AddDays((weekNumber - 1) * 7);
            var weekEnd = weekStart.AddDays(6);

            var hasMatchInWeek = matchSet is not null && matchSet.Any(m => m >= weekStart && m <= weekEnd);
            var isDeloadWeek = totalWeeks >= 4 && weekNumber == totalWeeks;

            var weekSessions = new List<PlanDraftSessionDto>(sessionCount);

            for (var index = 0; index < sessionCount; index++)
            {
                var blueprint = sessionBlueprints[index];
                if (blueprint.Exercises.Count == 0)
                {
                    continue;
                }

                var dayOfWeek = trainingDays[index];
                var sessionDate = ResolveSessionDate(weekStart, dayOfWeek);

                // Día de partido (D): no se programa sesión de pesas
                if (matchSet is not null &&
                    matchSet.Contains(sessionDate) &&
                    !MatchDayLoadRules.AllowStrengthSessionOnMatchDay)
                {
                    continue;
                }

                var (taperingCap, taperingNote) = ResolvePreMatchTapering(sessionDate, matchSet);
                var exercises = BuildSessionExercises(
                    blueprint,
                    profile,
                    weekNumber,
                    hasMatchInWeek,
                    isDeloadWeek,
                    taperingCap);

                var order = weekSessions.Count + 1;
                var sessionName = $"Día {order} · {blueprint.FocusName}";
                var coachNotes = BuildCoachNotes(
                    profile,
                    exercises,
                    hasMatchInWeek,
                    isDeloadWeek,
                    taperingNote,
                    blueprint.SubstitutionNotes);

                weekSessions.Add(new PlanDraftSessionDto(
                    Name: sessionName,
                    DayOfWeek: dayOfWeek,
                    Order: order,
                    CoachNotes: coachNotes,
                    Exercises: exercises));
            }

            resultWeeks.Add(new PlanDraftWeekDto(
                WeekNumber: weekNumber,
                Sessions: weekSessions));
        }

        return resultWeeks;
    }

    private static List<PlanDraftExerciseDto> BuildSessionExercises(
        RuleSessionBlueprint blueprint,
        RuleGoalProfile profile,
        int weekNumber,
        bool hasMatchInWeek,
        bool isDeloadWeek,
        decimal? taperingCap)
    {
        var exercises = new List<PlanDraftExerciseDto>(blueprint.Exercises.Count);
        var reduceWeekLoad = hasMatchInWeek || isDeloadWeek;

        for (var i = 0; i < blueprint.Exercises.Count; i++)
        {
            var order = i + 1;
            var catalogExercise = blueprint.Exercises[i];
            var prescription = RuleBasedGoalProfiles.GetPrescription(profile, order);

            var sets = prescription.Sets;
            var reps = prescription.Reps;

            // Progresión semana a semana: +2,5 puntos de %RM en el principal, sin pasar el tope del objetivo.
            // En descarga se parte del %RM base antes de aplicar la baja de -10 puntos.
            decimal targetPct;
            if (prescription.IsMain && !isDeloadWeek)
            {
                var progressed = prescription.BasePercentage + ((weekNumber - 1) * WeeklyIncrementPercentage);
                targetPct = Math.Min(progressed, prescription.MaxPercentage);
            }
            else
            {
                targetPct = prescription.BasePercentage;
            }

            if (reduceWeekLoad)
            {
                sets = Math.Max(1, sets - 1);
                targetPct = Math.Max(MinTargetPercentage, targetPct - ReducedWeekPercentagePenalty);
            }

            if (taperingCap.HasValue && targetPct > taperingCap.Value)
            {
                targetPct = taperingCap.Value;
            }

            targetPct = Math.Clamp(targetPct, 0m, 100m);

            exercises.Add(new PlanDraftExerciseDto(
                ExerciseId: catalogExercise.Id,
                Order: order,
                Sets: Math.Clamp(sets, 1, 10),
                Reps: Math.Clamp(reps, 1, 30),
                TargetPercentage: targetPct,
                ExerciseName: catalogExercise.Name,
                MuscleGroup: catalogExercise.MuscleGroup,
                IsReference: false));
        }

        return exercises;
    }

    private static string BuildCoachNotes(
        RuleGoalProfile profile,
        IReadOnlyList<PlanDraftExerciseDto> exercises,
        bool hasMatchInWeek,
        bool isDeloadWeek,
        string? taperingNote,
        IReadOnlyList<string> substitutionNotes)
    {
        var parts = new List<string> { WarmUpNote };

        var main = exercises.FirstOrDefault();
        var mainSets = main?.Sets ?? profile.MainSets;
        var mainReps = main?.Reps ?? profile.MainReps;
        var mainPct = main?.TargetPercentage ?? profile.MainBasePercentage;
        var formattedPct = mainPct.ToString("0.#", EsArCulture);
        var cueSuffix = !string.IsNullOrWhiteSpace(profile.ExecutionCue)
            ? $", {profile.ExecutionCue}"
            : string.Empty;

        parts.Add(
            $"Armado por reglas (sin IA disponible) · Objetivo: {profile.DisplayName} · " +
            $"{mainSets} × {mainReps} al {formattedPct} %{cueSuffix} en el principal, {profile.RestNote} de descanso.");

        if (hasMatchInWeek)
        {
            parts.Add("Semana de partido: carga reducida.");
        }

        if (!string.IsNullOrWhiteSpace(taperingNote))
        {
            parts.Add(taperingNote);
        }

        if (isDeloadWeek)
        {
            parts.Add("Semana de descarga: carga reducida (-1 serie, -10 %RM).");
        }

        foreach (var note in substitutionNotes)
        {
            if (!string.IsNullOrWhiteSpace(note))
                parts.Add(note);
        }

        return string.Join(" ", parts);
    }

    private static (decimal? CapPercentage, string? Note) ResolvePreMatchTapering(
        DateOnly sessionDate,
        HashSet<DateOnly>? matchSet)
    {
        if (matchSet is null)
            return (null, null);

        if (matchSet.Contains(sessionDate.AddDays(1)))
        {
            var cap = MatchDayLoadRules.MaxPercentageOneDayBeforeMatch;
            var formattedCap = cap.ToString("0.#", EsArCulture);
            return (cap, $"Tapering D-1: carga limitada al {formattedCap} % por partido oficial.");
        }

        if (matchSet.Contains(sessionDate.AddDays(2)))
        {
            var cap = MatchDayLoadRules.MaxPercentageTwoDaysBeforeMatch;
            var formattedCap = cap.ToString("0.#", EsArCulture);
            return (cap, $"Tapering D-2: carga moderada al {formattedCap} % por proximidad de partido.");
        }

        return (null, null);
    }

    private static DateOnly ResolveSessionDate(DateOnly weekStart, DayOfWeek dayOfWeek)
    {
        for (var i = 0; i < 7; i++)
        {
            var candidate = weekStart.AddDays(i);
            if (candidate.DayOfWeek == dayOfWeek)
                return candidate;
        }

        return weekStart;
    }
}
