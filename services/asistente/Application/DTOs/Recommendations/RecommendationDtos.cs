namespace Application.DTOs.Recommendations;

/// <summary>
/// Métrica del perfil físico del atleta comparada frente al promedio de su grupo o categoría (#083 · KAN-378).
/// Los valores numéricos provienen ya calculados por reglas determinísticas.
/// </summary>
public sealed record ProfileMetricComparisonDto(
    string Dimension,
    double AthleteValue,
    double GroupAverage,
    string Unit,
    double? PercentageDifference = null,
    string? Trend = null);

/// <summary>
/// Solicitud para redactar una recomendación de entrenamiento para el preparador físico
/// a partir de la comparación del perfil del atleta frente al grupo y sus tendencias (#083 · KAN-378).
/// </summary>
public sealed record TrainingRecommendationRequestDto(
    string Reason,
    IReadOnlyList<ProfileMetricComparisonDto>? Metrics = null,
    string? CategoryName = null,
    string? SuggestedFocus = null,
    Guid? AthleteId = null);

/// <summary>
/// Recomendación de entrenamiento redactada y validada para revisión del preparador físico (#083 · KAN-378).
/// </summary>
public sealed record TrainingRecommendationResponseDto(
    string Title,
    string Reason,
    string RecommendationText,
    string ProviderUsed = "Reglas");

/// <summary>
/// Solicitud para redactar una recomendación de hábitos y alimentación (#083 · KAN-378).
/// La IA únicamente redacta: los números (proteína, hidratación, sueño) vienen calculados por reglas
/// y los alimentos provienen exclusivamente de la lista aprobada.
/// Incluye contexto de seguridad (menor, peso/objetivo, momentos, datos faltantes, derivación).
/// Campos de peso/objetivo/rangos pueden ser desconocidos si el modelo de datos aún no los provee (TODO).
/// </summary>
public sealed record NutritionRecommendationRequestDto(
    string AthleticGoal,
    IReadOnlyList<string>? ApprovedFoods = null,
    string? Moment = null,
    double? MinProteinGrams = null,
    double? MaxProteinGrams = null,
    double? DailyProteinRequirementGrams = null,
    double? HydrationLiters = null,
    double? SleepHours = null,
    bool IncludeQuantities = true,
    Guid? AthleteId = null,
    // Opcionales, backward-compatible
    double? WeightKg = null,
    bool IsMinor = false,
    string? RecommendationMode = null, // Quantitative | Qualitative | Referral
    bool RequiresProfessionalReferral = false,
    IReadOnlyList<string>? ReferralReasons = null,
    IReadOnlyList<string>? DetectedMoments = null,
    IReadOnlyList<string>? MissingData = null);

/// <summary>
/// Recomendación de hábitos y alimentación redactada y validada (#083 · KAN-378).
/// Nunca menciona suplementos ni cantidades para bajar de peso y utiliza únicamente alimentos de la lista aprobada.
/// </summary>
public sealed record NutritionRecommendationResponseDto(
    string SuggestedPlateComposition,
    string HydrationAndSleepGuidelines,
    IReadOnlyList<string> MentionedFoods,
    string LegalDisclaimer,
    string ProviderUsed = "Reglas",
    // Opcionales, backward-compatible
    double? MinProteinGrams = null,
    double? MaxProteinGrams = null,
    string? RecommendationMode = null,
    bool RequiresProfessionalReferral = false,
    IReadOnlyList<string>? ReferralReasons = null,
    IReadOnlyList<string>? DetectedMoments = null,
    IReadOnlyList<string>? MissingData = null);

/// <summary>
/// Resultado de la validación de esquema y de reglas de seguridad sobre la salida generada por la IA (#083 · KAN-378).
/// </summary>
public sealed record RecommendationValidationResult<T>(
    bool IsValid,
    T? Value,
    string ValidationCode,
    string? ErrorMessage = null,
    bool IsSyntaxError = false)
{
    public static RecommendationValidationResult<T> Success(T value) =>
        new(true, value, "Valido");

    public static RecommendationValidationResult<T> Failure(
        string validationCode,
        string errorMessage,
        bool isSyntaxError = false) =>
        new(false, default, validationCode, errorMessage, isSyntaxError);
}
