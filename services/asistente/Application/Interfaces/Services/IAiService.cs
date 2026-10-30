namespace Application.Interfaces.Services;

using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;

public interface IAiService
{
    Task<RoutineSuggestionResponseDto> SuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default);

    Task<(PlanDraftContentDto Content, string Provider)> GeneratePlanDraftAsync(
        CreatePlanDraftRequestDto request,
        IReadOnlyList<DateOnly>? matchDates = null,
        CancellationToken cancellationToken = default);
}
