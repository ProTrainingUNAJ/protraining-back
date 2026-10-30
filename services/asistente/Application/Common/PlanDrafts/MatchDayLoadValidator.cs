using System.Globalization;
using Application.DTOs.PlanDrafts;
using Application.Interfaces.Services;
using Domain.Common;

namespace Application.Common.PlanDrafts;

/// <summary>
/// Validador algorítmico que corrige cargas pre-partido (D-1 <= 60 %, D-2 <= 75 %) y quita sesiones
/// de fuerza el día del partido (D), anotando las correcciones en coachNotes y devolviendo los avisos (KAN-408 / KAN-410).
/// </summary>
public sealed class MatchDayLoadValidator : IMatchDayLoadValidator
{
    public MatchDayValidationResult ValidateAndAdjust(
        PlanDraftContentDto content,
        DateOnly startDate,
        IReadOnlyList<DateOnly> matchDates,
        IReadOnlyDictionary<Guid, string>? exerciseNames = null)
    {
        if (matchDates is null || matchDates.Count == 0 || content.Weeks is null || content.Weeks.Count == 0)
        {
            return new MatchDayValidationResult(content, []);
        }

        var matchSet = matchDates.ToHashSet();
        var corrections = new List<string>();
        var adjustedWeeks = new List<PlanDraftWeekDto>(content.Weeks.Count);

        foreach (var week in content.Weeks)
        {
            if (week.Sessions is null || week.Sessions.Count == 0)
            {
                adjustedWeeks.Add(week);
                continue;
            }

            var adjustedSessions = new List<PlanDraftSessionDto>();

            foreach (var session in week.Sessions)
            {
                var sessionDate = GetSessionDate(startDate, week.WeekNumber, session.DayOfWeek);

                // Regla D (mismo día del partido): sin sesión de fuerza
                if (!MatchDayLoadRules.AllowStrengthSessionOnMatchDay && matchSet.Contains(sessionDate))
                {
                    var matchLabel = FormatDateSpanish(sessionDate);
                    corrections.Add(
                        $"Se quitó la sesión '{session.Name}' (semana {week.WeekNumber}) por coincidir con el partido del {matchLabel}.");
                    continue;
                }

                // Evaluar si la sesión cae en D-1 (día anterior al partido) o D-2 (dos días antes)
                DateOnly? governingMatchDate = null;
                decimal? maxAllowedPct = null;

                var oneDayAfter = sessionDate.AddDays(1);
                var twoDaysAfter = sessionDate.AddDays(2);

                if (matchSet.Contains(oneDayAfter))
                {
                    governingMatchDate = oneDayAfter;
                    maxAllowedPct = MatchDayLoadRules.GetMaxAllowedPercentage(1);
                }
                else if (matchSet.Contains(twoDaysAfter))
                {
                    governingMatchDate = twoDaysAfter;
                    maxAllowedPct = MatchDayLoadRules.GetMaxAllowedPercentage(2);
                }

                if (!governingMatchDate.HasValue || !maxAllowedPct.HasValue || session.Exercises is null)
                {
                    adjustedSessions.Add(session);
                    continue;
                }

                var sessionCorrections = new List<string>();
                var adjustedExercises = new List<PlanDraftExerciseDto>(session.Exercises.Count);
                var matchDateText = FormatDateSpanish(governingMatchDate.Value);

                foreach (var exercise in session.Exercises)
                {
                    if (exercise.TargetPercentage.HasValue && exercise.TargetPercentage.Value > maxAllowedPct.Value)
                    {
                        var originalPct = exercise.TargetPercentage.Value;
                        var cappedPct = maxAllowedPct.Value;
                        var exerciseLabel = ResolveExerciseLabel(exercise.ExerciseId, exerciseNames);

                        var note = string.Create(
                            CultureInfo.InvariantCulture,
                            $"Ajustado por el partido del {matchDateText}: {exerciseLabel} {originalPct:0.##} % → {cappedPct:0.##} %");

                        sessionCorrections.Add(note);
                        corrections.Add(note);
                        adjustedExercises.Add(exercise with { TargetPercentage = cappedPct });
                    }
                    else
                    {
                        adjustedExercises.Add(exercise);
                    }
                }

                var updatedCoachNotes = AppendCoachNotes(session.CoachNotes, sessionCorrections);
                adjustedSessions.Add(session with
                {
                    CoachNotes = updatedCoachNotes,
                    Exercises = adjustedExercises
                });
            }

            var reorderedSessions = adjustedSessions
                .Select((s, index) => s with { Order = index + 1 })
                .ToList();

            adjustedWeeks.Add(week with { Sessions = reorderedSessions });
        }

        var adjustedContent = content with { Weeks = adjustedWeeks };
        return new MatchDayValidationResult(adjustedContent, corrections);
    }

    public static DateOnly GetSessionDate(DateOnly startDate, int weekNumber, DayOfWeek sessionDayOfWeek)
    {
        var weekStart = startDate.AddDays((weekNumber - 1) * 7);
        var daysOffset = ((int)sessionDayOfWeek - (int)weekStart.DayOfWeek + 7) % 7;
        return weekStart.AddDays(daysOffset);
    }

    private static string ResolveExerciseLabel(Guid exerciseId, IReadOnlyDictionary<Guid, string>? exerciseNames)
    {
        if (exerciseNames is not null &&
            exerciseNames.TryGetValue(exerciseId, out var name) &&
            !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return $"ejercicio {exerciseId}";
    }

    private static string? AppendCoachNotes(string? existingNotes, IReadOnlyList<string> sessionCorrections)
    {
        if (sessionCorrections.Count == 0)
            return existingNotes;

        var joinedCorrections = string.Join(" | ", sessionCorrections);
        if (string.IsNullOrWhiteSpace(existingNotes))
            return joinedCorrections;

        return $"{existingNotes.Trim()} | {joinedCorrections}";
    }

    private static string FormatDateSpanish(DateOnly date)
    {
        var dayName = date.DayOfWeek switch
        {
            DayOfWeek.Monday => "lunes",
            DayOfWeek.Tuesday => "martes",
            DayOfWeek.Wednesday => "miércoles",
            DayOfWeek.Thursday => "jueves",
            DayOfWeek.Friday => "viernes",
            DayOfWeek.Saturday => "sábado",
            DayOfWeek.Sunday => "domingo",
            _ => "día"
        };

        return $"{dayName} {date.Day}/{date.Month}";
    }
}
