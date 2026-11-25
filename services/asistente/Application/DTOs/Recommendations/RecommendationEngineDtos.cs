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

/// <summary>
/// Solicitud para editar el título y texto de una recomendación pendiente (#197 · KAN-492).
/// El motivo (reason) se mantiene inmutable como evidencia objetiva calculada por el motor.
/// </summary>
public sealed record UpdateRecommendationRequestDto(
    string Title,
    string RecommendationText);

/// <summary>
/// Solicitud opcional para editar y aprobar una recomendación en un solo paso (#197 · KAN-492).
/// </summary>
public sealed record ApproveRecommendationRequestDto(
    string? Title = null,
    string? RecommendationText = null);

/// <summary>
/// Recomendación aprobada por el preparador físico visible para el atleta en su app (#197 · KAN-492).
/// </summary>
public sealed record MyRecommendationResponseDto(
    Guid Id,
    Guid? CategoryId,
    string? CategoryName,
    string Type,
    string Title,
    string Reason,
    string RecommendationText,
    DateTime? ReviewedAtUtc,
    DateTime CreatedAt,
    // Aviso del nutricionista, aparte del texto para que el front lo destaque (KAN-634). Solo en las de hábitos.
    string? LegalDisclaimer = null);

/// <summary>
/// Respuesta de recomendación de hábitos/nutrición para validación del PF (contrato hábitos extendido).
/// Campos adicionales son opcionales para compatibilidad; algunos pueden ser null cuando faltan datos o no aplica.
/// TODO: alinear completamente con contrato/consumidores si se agregan nuevos campos obligatorios.
/// </summary>
public sealed record HabitsRecommendationResponseDto(
    Guid Id,
    Guid AthleteId,
    string AthleticGoal,
    double? DailyProteinRequirementGrams,
    string SuggestedPlateComposition,
    string HydrationAndSleepGuidelines,
    string LegalDisclaimer,
    bool ApprovedByPf,
    double? MinProteinGrams = null,
    double? MaxProteinGrams = null,
    string? RecommendationMode = null,
    bool IsMinor = false,
    bool RequiresProfessionalReferral = false,
    IReadOnlyList<string>? ReferralReasons = null,
    IReadOnlyList<string>? DetectedMoments = null,
    IReadOnlyList<string>? MissingData = null,
    double? WeightKg = null,
    string? Provider = null,
    string? Status = null);

/// <summary>
/// Snapshot persistido en MetricsSnapshotJson para recomendaciones de hábitos.
/// Permite reconstruir la respuesta de contrato sin cambios de esquema de BD.
/// </summary>
public sealed record HabitsRecommendationSnapshotDto(
    string AthleticGoal,
    double? WeightKg,
    double? MinProteinGrams,
    double? MaxProteinGrams,
    double? DailyProteinRequirementGrams,
    string SuggestedPlateComposition,
    string HydrationAndSleepGuidelines,
    string LegalDisclaimer,
    string? RecommendationMode,
    bool IsMinor,
    bool RequiresProfessionalReferral,
    IReadOnlyList<string>? ReferralReasons,
    IReadOnlyList<string>? DetectedMoments,
    IReadOnlyList<string>? MissingData,
    IReadOnlyList<string>? MentionedFoods,
    string? CalculationNote);

public sealed record ApproveHabitsRecommendationResponseDto(string Message);
