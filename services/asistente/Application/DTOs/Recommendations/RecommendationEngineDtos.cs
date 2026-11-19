namespace Application.DTOs.Recommendations;

/// <summary>
/// Parámetros opcionales para ejecutar el motor de recomendaciones sobre un atleta (#196 · KAN-491).
/// </summary>
public sealed record GenerateAthleteRecommendationsRequestDto(
    Guid? CategoryId = null,
    int? Weeks = 4);

/// <summary>
/// Parámetros opcionales para ejecutar el motor de recomendaciones sobre toda una categoría (#196 · KAN-491).
/// </summary>
public sealed record GenerateCategoryRecommendationsRequestDto(
    int? Weeks = 4);

/// <summary>
/// Resumen determinístico de tendencias semanales de bienestar y carga del atleta (#196 · KAN-491).
/// </summary>
public sealed record WeeklyTrendsSummaryDto(
    int WeeksEvaluated,
    double? LatestWellness,
    double? PreviousWellness,
    double? WellnessDelta,
    int? LatestTotalLoad,
    double? PreviousAverageLoad,
    double? LoadChangePercent,
    string TrendLabel);

/// <summary>
/// Recomendación persistida para revisión y aprobación del preparador físico (#196 · KAN-491).
/// </summary>
public sealed record AthleteRecommendationResponseDto(
    Guid Id,
    Guid AthleteId,
    Guid? CategoryId,
    string? CategoryName,
    string Type,
    string Status,
    string Title,
    string Reason,
    string RecommendationText,
    string Provider,
    IReadOnlyList<ProfileMetricComparisonDto> Metrics,
    WeeklyTrendsSummaryDto? Trends,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ReviewedAtUtc);

/// <summary>
/// Resultado de ejecutar el motor de recomendaciones sobre un atleta (#196 · KAN-491).
/// </summary>
public sealed record RunAthleteRecommendationEngineResponseDto(
    Guid AthleteId,
    Guid? CategoryId,
    string? CategoryName,
    int GeneratedCount,
    int ExistingPendingCount,
    IReadOnlyList<AthleteRecommendationResponseDto> Recommendations);

/// <summary>
/// Resultado de ejecutar el motor de recomendaciones sobre toda una categoría (#196 · KAN-491).
/// </summary>
public sealed record RunCategoryRecommendationEngineResponseDto(
    Guid CategoryId,
    string CategoryName,
    int EvaluatedAthletes,
    int GeneratedCount,
    int ExistingPendingCount,
    IReadOnlyList<AthleteRecommendationResponseDto> Recommendations);
