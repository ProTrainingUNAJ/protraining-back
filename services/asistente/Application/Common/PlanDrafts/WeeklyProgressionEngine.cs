namespace Application.Common.PlanDrafts;

using Application.DTOs.PlanDrafts;
using Domain.Common;

/// <summary>
/// Motor determinístico en C# encargado de calcular la progresión semanal de cargas y volúmenes (semanas 2 a N).
/// La IA nunca calcula la progresión: diseña la sesión modelo y este motor proyecta la periodización deportiva exacta.
/// </summary>
public static class WeeklyProgressionEngine
{
    /// <summary>
    /// Genera la lista completa de semanas proyectadas a partir de las sesiones modelo de la semana 1.
    /// </summary>
    public static List<PlanDraftWeekDto> GenerateProgressionWeeks(
        IReadOnlyList<PlanDraftSessionDto> modelSessions,
        int totalWeeks,
        DateOnly? startDate = null,
        IReadOnlyList<DateOnly>? matchDates = null)
    {
        if (modelSessions == null || modelSessions.Count == 0)
            return [];

        totalWeeks = Math.Clamp(totalWeeks, 1, 12);
        var baseDate = startDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var matchSet = matchDates != null && matchDates.Count > 0
            ? new HashSet<DateOnly>(matchDates)
            : null;

        var resultWeeks = new List<PlanDraftWeekDto>(totalWeeks);

        for (var weekNumber = 1; weekNumber <= totalWeeks; weekNumber++)
        {
            var weekStart = baseDate.AddDays((weekNumber - 1) * 7);
            var sessions = new List<PlanDraftSessionDto>(modelSessions.Count);

            foreach (var baseSession in modelSessions)
            {
                var sessionDate = ResolveSessionDate(weekStart, baseSession.DayOfWeek);

                // Si coincide con día de partido (D), se omite si la regla deportiva no lo permite
                if (matchSet != null && matchSet.Contains(sessionDate) && !MatchDayLoadRules.AllowStrengthSessionOnMatchDay)
                {
                    continue;
                }

                var (adjustedExercises, coachNotes) = CalculateSessionProgression(
                    baseSession,
                    weekNumber,
                    totalWeeks,
                    sessionDate,
                    matchSet);

                sessions.Add(baseSession with
                {
                    CoachNotes = coachNotes,
                    Exercises = adjustedExercises
                });
            }

            resultWeeks.Add(new PlanDraftWeekDto(
                WeekNumber: weekNumber,
                Sessions: sessions
            ));
        }

        return resultWeeks;
    }

    private static (List<PlanDraftExerciseDto> Exercises, string CoachNotes) CalculateSessionProgression(
        PlanDraftSessionDto baseSession,
        int weekNumber,
        int totalWeeks,
        DateOnly sessionDate,
        HashSet<DateOnly>? matchSet)
    {
        var exercises = new List<PlanDraftExerciseDto>(baseSession.Exercises.Count);

        // Determinación del tipo de microciclo según la semana
        var isDeloadWeek = IsDeloadWeek(weekNumber, totalWeeks);
        var isPeakWeek = IsPeakWeek(weekNumber, totalWeeks);

        // Factores de progresión determinísticos en C#
        var (intensityDelta, setsModifier, notePrefix) = GetProgressionModifiers(weekNumber, totalWeeks, isDeloadWeek, isPeakWeek);

        // Control de tapering pre-partido si hay partido en D+1 o D+2
        decimal? matchCapPercentage = null;
        string? matchNote = null;
        if (matchSet != null)
        {
            if (matchSet.Contains(sessionDate.AddDays(1)))
            {
                matchCapPercentage = MatchDayLoadRules.MaxPercentageOneDayBeforeMatch;
                matchNote = $"[Tapering D-1: carga limitada al {matchCapPercentage:0.#}% por partido oficial]";
            }
            else if (matchSet.Contains(sessionDate.AddDays(2)))
            {
                matchCapPercentage = MatchDayLoadRules.MaxPercentageTwoDaysBeforeMatch;
                matchNote = $"[Tapering D-2: carga moderada al {matchCapPercentage:0.#}% por proximidad de partido]";
            }
        }

        foreach (var exercise in baseSession.Exercises)
        {
            var adjustedSets = exercise.Sets;
            var adjustedReps = exercise.Reps;
            decimal? adjustedPercentage = exercise.TargetPercentage;

            if (adjustedPercentage.HasValue && adjustedPercentage.Value > 0)
            {
                var newPercentage = adjustedPercentage.Value + intensityDelta;

                // Redondeo determinístico a pasos de 2.5%
                newPercentage = RoundToQuarterStep(newPercentage);

                // En semana de pico se puede reducir 1 o 2 reps para soportar mayor intensidad
                if (isPeakWeek && exercise.Reps > 6 && exercise.Order <= 2)
                {
                    adjustedReps = Math.Max(5, exercise.Reps - 2);
                }

                // En semana de descarga se reduce volumen de series
                if (isDeloadWeek)
                {
                    adjustedSets = Math.Max(2, exercise.Sets - 1);
                    newPercentage = Math.Min(newPercentage, 65m);
                }

                // Aplicar techo si hay partido oficial próximo
                if (matchCapPercentage.HasValue && newPercentage > matchCapPercentage.Value)
                {
                    newPercentage = matchCapPercentage.Value;
                }

                adjustedPercentage = Math.Clamp(newPercentage, 40m, 95m);
            }
            else if (isDeloadWeek)
            {
                // En ejercicios accesorios sin porcentaje se reduce una serie
                adjustedSets = Math.Max(2, exercise.Sets - 1);
            }

            exercises.Add(exercise with
            {
                Sets = adjustedSets,
                Reps = adjustedReps,
                TargetPercentage = adjustedPercentage
            });
        }

        var notes = string.IsNullOrWhiteSpace(baseSession.CoachNotes)
            ? notePrefix
            : $"{baseSession.CoachNotes}. {notePrefix}";

        if (!string.IsNullOrWhiteSpace(matchNote))
        {
            notes = $"{notes} {matchNote}";
        }

        return (exercises, notes.Trim());
    }

    private static (decimal IntensityDelta, int SetsModifier, string NotePrefix) GetProgressionModifiers(
        int weekNumber,
        int totalWeeks,
        bool isDeloadWeek,
        bool isPeakWeek)
    {
        if (isDeloadWeek)
        {
            return (-15m, -1, "Semana de descarga (Deload): volumen e intensidad reducidos para supercompensación");
        }

        if (isPeakWeek)
        {
            return (+5m, 0, "Semana de sobrecarga pico: máxima exigencia neuromuscular y técnica sólida");
        }

        return weekNumber switch
        {
            1 => (0m, 0, "Semana 1 (Base): volumen inicial y control técnico de movimientos"),
            2 => (+2.5m, 0, "Semana 2 (Progresión): incremento gradual de sobrecarga (+2.5% RM)"),
            _ => (+5m, 0, $"Semana {weekNumber} (Sobrecarga): avance progresivo planificado")
        };
    }

    private static bool IsDeloadWeek(int weekNumber, int totalWeeks)
    {
        if (totalWeeks < 3)
            return false;

        // Si son 4 semanas, la semana 4 es descarga. Si son 8, la 4 y la 8.
        return weekNumber % 4 == 0 || (totalWeeks == 3 && weekNumber == 3);
    }

    private static bool IsPeakWeek(int weekNumber, int totalWeeks)
    {
        if (totalWeeks < 3)
            return false;

        // La semana inmediatamente anterior a la descarga es el pico
        return (weekNumber + 1) % 4 == 0 || (totalWeeks == 4 && weekNumber == 3);
    }

    private static decimal RoundToQuarterStep(decimal value)
    {
        // Redondea al múltiplo de 2.5 más cercano
        return Math.Round(value / 2.5m, MidpointRounding.AwayFromZero) * 2.5m;
    }

    private static DateOnly ResolveSessionDate(DateOnly weekStart, DayOfWeek dayOfWeek)
    {
        for (var i = 0; i < 7; i++)
        {
            var candidate = weekStart.AddDays(i);
            if (candidate.DayOfWeek == dayOfWeek)
            {
                return candidate;
            }
        }
        return weekStart;
    }
}
