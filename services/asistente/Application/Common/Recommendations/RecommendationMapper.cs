using System.Text.Json;
using Application.DTOs.Recommendations;
using Domain.Entities;

namespace Application.Common.Recommendations;

/// <summary>
/// Mapea y serializa el snapshot analítico de métricas y tendencias para las recomendaciones (#196 · KAN-491).
/// </summary>
public static class RecommendationMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private sealed record RecommendationSnapshot(
        IReadOnlyList<ProfileMetricComparisonDto>? Metrics,
        WeeklyTrendsSummaryDto? Trends);

    public static string SerializeSnapshot(
        IReadOnlyList<ProfileMetricComparisonDto>? metrics,
        WeeklyTrendsSummaryDto? trends) =>
        JsonSerializer.Serialize(new RecommendationSnapshot(metrics ?? [], trends), JsonOptions);

    public static (IReadOnlyList<ProfileMetricComparisonDto> Metrics, WeeklyTrendsSummaryDto? Trends) DeserializeSnapshot(
        string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return ([], null);

        try
        {
            var snapshot = JsonSerializer.Deserialize<RecommendationSnapshot>(snapshotJson, JsonOptions);
            return (snapshot?.Metrics ?? [], snapshot?.Trends);
        }
        catch
        {
            return ([], null);
        }
    }

    public static TrainingRecommendationRequestDto BuildTrainingAiRequest(
        Guid athleteId,
        string? categoryName,
        RecommendationSignal signal) =>
        new(
            Reason: signal.Reason,
            Metrics: signal.Metrics,
            CategoryName: categoryName,
            SuggestedFocus: signal.SuggestedFocus,
            AthleteId: athleteId);

    public static AthleteRecommendation CreatePendingTrainingEntity(
        Guid athleteId,
        Guid? categoryId,
        string? categoryName,
        RecommendationSignal signal,
        TrainingRecommendationResponseDto drafted) =>
        AthleteRecommendation.Create(
            athleteId: athleteId,
            title: drafted.Title,
            reason: drafted.Reason,
            recommendationText: drafted.RecommendationText,
            categoryId: categoryId,
            categoryName: categoryName,
            type: RecommendationType.Training,
            metricsSnapshotJson: SerializeSnapshot(signal.Metrics, signal.Trends),
            provider: drafted.ProviderUsed);

    public static AthleteRecommendationResponseDto ToResponseDto(AthleteRecommendation entity)
    {
        var (metrics, trends) = DeserializeSnapshot(entity.MetricsSnapshotJson);

        return new AthleteRecommendationResponseDto(
            Id: entity.Id,
            AthleteId: entity.AthleteId,
            CategoryId: entity.CategoryId,
            CategoryName: entity.CategoryName,
            Type: entity.Type.ToString(),
            Status: entity.Status.ToString(),
            Title: entity.Title,
            Reason: entity.Reason,
            RecommendationText: entity.RecommendationText,
            Provider: entity.Provider,
            Metrics: metrics,
            Trends: trends,
            CreatedAt: entity.CreatedAt,
            UpdatedAt: entity.UpdatedAt,
            ReviewedAtUtc: entity.ReviewedAtUtc);
    }
}
