using System.Text.Json;
using Application.DTOs.Recommendations;
using Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Application.Common.Recommendations;

/// <summary>
/// Mapea y serializa el snapshot analítico de métricas y tendencias para las recomendaciones (#196 · KAN-491).
/// Extendido para hábitos/nutrición almacenando detalles determinísticos en el snapshot JSON.
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
        WeeklyTrendsSummaryDto? Trends,
        HabitsRecommendationSnapshotDto? Habits = null);

    public static string SerializeSnapshot(
        IReadOnlyList<ProfileMetricComparisonDto>? metrics,
        WeeklyTrendsSummaryDto? trends) =>
        JsonSerializer.Serialize(new RecommendationSnapshot(metrics ?? [], trends), JsonOptions);

    public static string SerializeHabitsSnapshot(HabitsRecommendationSnapshotDto habits) =>
        JsonSerializer.Serialize(new RecommendationSnapshot([], null, habits), JsonOptions);

    public static (IReadOnlyList<ProfileMetricComparisonDto> Metrics, WeeklyTrendsSummaryDto? Trends) DeserializeSnapshot(
        string? snapshotJson,
        ILogger? logger = null)
    {
        var full = DeserializeFullSnapshot(snapshotJson, logger);
        return (full.Metrics, full.Trends);
    }

    public static (IReadOnlyList<ProfileMetricComparisonDto> Metrics, WeeklyTrendsSummaryDto? Trends, HabitsRecommendationSnapshotDto? Habits) DeserializeFullSnapshot(
        string? snapshotJson,
        ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return ([], null, null);

        try
        {
            var snapshot = JsonSerializer.Deserialize<RecommendationSnapshot>(snapshotJson, JsonOptions);
            return (snapshot?.Metrics ?? [], snapshot?.Trends, snapshot?.Habits);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "No se pudo deserializar el snapshot de métricas de la recomendación.");
            return ([], null, null);
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
            AthleteId: athleteId,
            IsRecurringAfterDiscard: signal.IsRecurringAfterDiscard,
            RecurringTestName: signal.RecurringTestName,
            NewMeasurementsSinceDiscard: signal.NewMeasurementsSinceDiscard);

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

    public static AthleteRecommendationResponseDto ToResponseDto(AthleteRecommendation entity) =>
        ToResponseDto(entity, logger: null);

    public static AthleteRecommendationResponseDto ToResponseDto(
        AthleteRecommendation entity,
        ILogger? logger)
    {
        var (metrics, trends) = DeserializeSnapshot(entity.MetricsSnapshotJson, logger);

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

    public static AthleteRecommendation CreatePendingHabitsEntity(
        Guid athleteId,
        Guid? categoryId,
        string? categoryName,
        string title,
        string reason,
        string recommendationText,
        HabitsRecommendationSnapshotDto snapshot,
        string? provider = null) =>
        AthleteRecommendation.Create(
            athleteId: athleteId,
            title: title,
            reason: reason,
            recommendationText: recommendationText,
            categoryId: categoryId,
            categoryName: categoryName,
            type: RecommendationType.Habits,
            metricsSnapshotJson: SerializeHabitsSnapshot(snapshot),
            provider: provider);

    /// <summary>
    /// Proyecta una recomendación de hábitos persistida (pendiente o aprobada) al DTO del contrato.
    /// Se usa tanto desde la Query de lectura como desde el Command de generación.
    /// </summary>
    public static HabitsRecommendationResponseDto ToHabitsResponse(
        AthleteRecommendation entity,
        bool? knownIsMinor = null)
    {
        var (_, _, habits) = DeserializeFullSnapshot(entity.MetricsSnapshotJson);
        var isMinor = habits?.IsMinor ?? knownIsMinor ?? false;
        var daily = habits?.DailyProteinRequirementGrams;
        if (daily is null && habits?.MinProteinGrams is not null && habits?.MaxProteinGrams is not null)
            daily = (habits.MinProteinGrams.Value + habits.MaxProteinGrams.Value) / 2.0;

        return new HabitsRecommendationResponseDto(
            Id: entity.Id,
            AthleteId: entity.AthleteId,
            AthleticGoal: habits?.AthleticGoal ?? "Objetivo no informado",
            DailyProteinRequirementGrams: daily,
            SuggestedPlateComposition: habits?.SuggestedPlateComposition ?? entity.RecommendationText,
            HydrationAndSleepGuidelines: habits?.HydrationAndSleepGuidelines ?? string.Empty,
            LegalDisclaimer: habits?.LegalDisclaimer ?? RecommendationPromptBuilder.OfficialNutritionDisclaimer,
            ApprovedByPf: entity.Status == RecommendationStatus.Approved,
            MinProteinGrams: habits?.MinProteinGrams,
            MaxProteinGrams: habits?.MaxProteinGrams,
            RecommendationMode: habits?.RecommendationMode,
            IsMinor: isMinor,
            RequiresProfessionalReferral: habits?.RequiresProfessionalReferral ?? false,
            ReferralReasons: habits?.ReferralReasons ?? [],
            DetectedMoments: habits?.DetectedMoments ?? [],
            MissingData: habits?.MissingData ?? [],
            WeightKg: habits?.WeightKg,
            Provider: entity.Provider,
            Status: entity.Status.ToString());
    }

    public static MyRecommendationResponseDto ToMyResponseDto(AthleteRecommendation entity) =>
        new(
            Id: entity.Id,
            CategoryId: entity.CategoryId,
            CategoryName: entity.CategoryName,
            Type: entity.Type.ToString(),
            Title: entity.Title,
            Reason: entity.Reason,
            RecommendationText: entity.RecommendationText,
            ReviewedAtUtc: entity.ReviewedAtUtc,
            CreatedAt: entity.CreatedAt);
}
