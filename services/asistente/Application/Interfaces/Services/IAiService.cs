namespace Application.Interfaces.Services;

using Application.Common.DayAdjustments;
using Application.Common.Recommendations;
using Application.DTOs.Ai;
using Application.DTOs.DayAdjustments;
using Application.DTOs.PlanDrafts;
using Application.DTOs.Recommendations;

public interface IAiService
{
    Task<RoutineSuggestionResponseDto> SuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default);

    Task<(PlanDraftContentDto Content, string Provider)> GeneratePlanDraftAsync(
        CreatePlanDraftRequestDto request,
        IReadOnlyList<DateOnly>? matchDates = null,
        CancellationToken cancellationToken = default);

    Task<(string Justification, string Provider)> GenerateDayAdjustmentJustificationAsync(
        IReadOnlyList<DayAdjustmentItemDto> items,
        CancellationToken cancellationToken = default) =>
        Task.FromResult((DayAdjustmentMapper.BuildDeterministicJustification(items), "Rules"));

    Task<TrainingRecommendationResponseDto> GenerateTrainingRecommendationAsync(
        TrainingRecommendationRequestDto request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(RecommendationPromptBuilder.BuildDeterministicTrainingRecommendation(request, "Reglas"));

    Task<NutritionRecommendationResponseDto> GenerateNutritionRecommendationAsync(
        NutritionRecommendationRequestDto request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(RecommendationPromptBuilder.BuildDeterministicNutritionRecommendation(request, "Reglas"));
}
