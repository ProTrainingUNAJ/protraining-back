namespace Application.Interfaces.Services;

using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;

public interface IAiClient
{
    string ProviderName { get; }

    Task<RoutineSuggestionResponseDto?> TrySuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default);

    Task<PlanDraftContentDto?> TryGeneratePlanDraftAsync(
        CreatePlanDraftRequestDto request,
        IReadOnlyList<DateOnly>? matchDates = null,
        CancellationToken cancellationToken = default);
}
