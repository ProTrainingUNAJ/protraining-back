using Application.DTOs.PlanDrafts;

namespace Application.Interfaces.Services;

public sealed record MatchDayValidationResult(
    PlanDraftContentDto Content,
    IReadOnlyList<string> Corrections
);

public interface IMatchDayLoadValidator
{
    MatchDayValidationResult ValidateAndAdjust(
        PlanDraftContentDto content,
        DateOnly startDate,
        IReadOnlyList<DateOnly> matchDates,
        IReadOnlyDictionary<Guid, string>? exerciseNames = null);
}
