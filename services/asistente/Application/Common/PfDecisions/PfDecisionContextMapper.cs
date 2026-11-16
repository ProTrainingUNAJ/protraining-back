using Application.DTOs.Ai;
using Domain.Entities;

namespace Application.Common.PfDecisions;

public static class PfDecisionContextMapper
{
    public static IReadOnlyList<PfDecisionContextDto> Map(IEnumerable<PfDecision> decisions)
    {
        return decisions
            .Select(d => new PfDecisionContextDto(
                d.Source,
                d.Date,
                d.Zone,
                d.SuggestedExerciseId,
                d.Decision,
                d.FinalExerciseId,
                d.FinalPercentage))
            .ToList();
    }
}
