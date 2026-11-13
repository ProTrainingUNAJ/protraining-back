using Application.DTOs.PlanDrafts;
using Domain.Entities;

namespace Application.Common.PlanDrafts;

/// <summary>
/// Detecta los cambios de ejercicios realizados por el PF sobre un borrador (sustitución por otro ejercicio
/// o eliminación) para guardarlos en la memoria del PF (KAN-410 / KAN-423).
/// Los cambios únicamente de series, repeticiones o porcentaje no generan registros en pf_decisions.
/// </summary>
public static class PfDecisionDiffExtractor
{
    public static IReadOnlyList<PfDecision> ExtractDecisions(
        Guid draftId,
        Guid? athleteId,
        PlanDraftContentDto previousContent,
        PlanDraftContentDto updatedContent)
    {
        var decisions = new List<PfDecision>();
        var previousWeeks = previousContent.Weeks ?? [];
        var updatedWeeks = updatedContent.Weeks ?? [];

        foreach (var prevWeek in previousWeeks)
        {
            var newWeek = updatedWeeks.FirstOrDefault(w => w.WeekNumber == prevWeek.WeekNumber);
            var prevSessions = prevWeek.Sessions ?? [];

            foreach (var prevSession in prevSessions)
            {
                var newSession = newWeek?.Sessions?.FirstOrDefault(s => s.Order == prevSession.Order)
                    ?? newWeek?.Sessions?.FirstOrDefault(s => s.DayOfWeek == prevSession.DayOfWeek);

                var prevExercises = prevSession.Exercises ?? [];
                if (newSession is null)
                {
                    foreach (var removedExercise in prevExercises)
                    {
                        if (removedExercise.ExerciseId == Guid.Empty)
                            continue;

                        decisions.Add(PfDecision.Create(
                            athleteId: athleteId,
                            source: PfDecisionSource.PlanDraft,
                            sourceId: draftId,
                            suggestedExerciseId: removedExercise.ExerciseId,
                            decision: PfDecisionType.Rejected));
                    }

                    continue;
                }

                var newExercises = newSession.Exercises ?? [];
                CompareSessionExercises(draftId, athleteId, prevExercises, newExercises, decisions);
            }
        }

        return decisions;
    }

    private static void CompareSessionExercises(
        Guid draftId,
        Guid? athleteId,
        IReadOnlyList<PlanDraftExerciseDto> prevExercises,
        IReadOnlyList<PlanDraftExerciseDto> newExercises,
        List<PfDecision> decisions)
    {
        var matchedNewIndices = new HashSet<int>();

        // 1. Marcar los ejercicios que se conservaron en la sesión (incluso si cambiaron series, reps o porcentaje)
        var unmatchedPrev = new List<PlanDraftExerciseDto>();
        foreach (var prev in prevExercises)
        {
            var matchIndex = -1;
            for (var i = 0; i < newExercises.Count; i++)
            {
                if (!matchedNewIndices.Contains(i) && newExercises[i].ExerciseId == prev.ExerciseId)
                {
                    matchIndex = i;
                    break;
                }
            }

            if (matchIndex >= 0)
            {
                matchedNewIndices.Add(matchIndex);
            }
            else if (prev.ExerciseId != Guid.Empty)
            {
                unmatchedPrev.Add(prev);
            }
        }

        // 2. Para cada ejercicio previo que ya no está, verificar si fue reemplazado en su mismo orden (Modified)
        // o si fue eliminado (Rejected)
        foreach (var prev in unmatchedPrev)
        {
            var replacementIndex = -1;
            for (var i = 0; i < newExercises.Count; i++)
            {
                if (!matchedNewIndices.Contains(i) && newExercises[i].Order == prev.Order)
                {
                    replacementIndex = i;
                    break;
                }
            }

            if (replacementIndex >= 0 && newExercises[replacementIndex].ExerciseId != Guid.Empty)
            {
                var replacement = newExercises[replacementIndex];
                matchedNewIndices.Add(replacementIndex);

                decisions.Add(PfDecision.Create(
                    athleteId: athleteId,
                    source: PfDecisionSource.PlanDraft,
                    sourceId: draftId,
                    suggestedExerciseId: prev.ExerciseId,
                    decision: PfDecisionType.Modified,
                    finalExerciseId: replacement.ExerciseId,
                    finalPercentage: replacement.TargetPercentage));
            }
            else
            {
                decisions.Add(PfDecision.Create(
                    athleteId: athleteId,
                    source: PfDecisionSource.PlanDraft,
                    sourceId: draftId,
                    suggestedExerciseId: prev.ExerciseId,
                    decision: PfDecisionType.Rejected));
            }
        }
    }
}
