using Application.DTOs.Recommendations;
using Application.Interfaces.Services;

namespace Application.Common.Recommendations;

/// <summary>
/// Reglas determinísticas de seguridad y cálculo para recomendaciones de hábitos y proteína.
/// Prioriza siempre la seguridad: menores sin cantidades, sin estrategias/cantidades para bajar de peso,
/// sin suplementos, derivación ante datos médicos/alergias/condiciones no verificables o riesgos,
/// y manejo explícito de datos faltantes. Backend autoritativo.
/// </summary>
/// <remarks>
/// Supuestos/TODOs pendientes de validación profesional o de modelo de datos:
/// - Peso y objetivo deportivo se leen del perfil físico canónico de Estructura (IHabitsAthleteContextProvider).
///   Si faltan o la fuente no responde, no se calcula rango cuantitativo y se reporta en missingData.
/// - Único rango validado en ticket: 70 kg + objetivo "ganar masa/músculo" => 100-140 g/día.
///   Otros objetivos/rangos (mantenimiento, rendimiento, etc.) requieren definición profesional (TODO).
/// - Umbral de "poco descanso" = &lt; 6 h en el último check-in disponible. Justificación: coincide con el
///   criterio de sueño de la alarma A ya implementada en OverloadPatternDetector (KAN-477, alerta High con
///   reducción de carga), donde SleepHours &lt; 6 se considera riesgo de recuperación. No se cambia el umbral
///   sin validación profesional (TODO).
/// - "Partido cercano" = fecha en [hoy, hoy + 2] desde hoy AR. Justificación: alineado con la ventana D-1/D-2
///   de MatchDayLoadRules (KAN-473), que ya limita la carga previa al partido. No se amplía sin validar (TODO).
/// - Momento de entrenamiento intenso: sin definición funcional aprobada ni fuente; se consume de
///   IHabitsTrainingDayContextProvider, hoy sin fuente, y se reporta en missingData. Nunca se infiere
///   de 1RM, carga, RPE ni objetivos de plan (TODO).
/// - Alergias/condiciones médicas se registran en Estructura como texto libre; HabitsMedicalContextRules las clasifica
///   en backend sin enviar texto clínico a la IA. Ficha vacía, inexistente o no verificable => derivación.
/// </remarks>
public static class HabitsRecommendationRules
{
    public const string ModeQuantitative = "Quantitative";
    public const string ModeQualitative = "Qualitative";
    public const string ModeReferral = "Referral";

    // Justificado por OverloadPatternDetector (KAN-477): SleepHours < 6 ya dispara alerta High. TODO: validar con equipo profesional antes de cambiarlo.
    public const double LowSleepThresholdHoursAssumption = 6.0;
    // Justificado por MatchDayLoadRules (KAN-473): ventana D-1/D-2 de carga previa al partido. TODO: validar con PO/PF antes de cambiarla.
    public const int MatchNearbyWindowDaysAssumption = 2;

    public static HabitsEvaluationResult Evaluate(HabitsEvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var moments = new List<string>();
        var missing = new List<string>();
        var referralReasons = new List<string>();

        if (input.ContextNotes is { Count: > 0 })
            missing.AddRange(input.ContextNotes);

        if (input.IsHeavyTrainingDay == true)
            moments.Add("Día pesado de entrenamiento");
        else if (input.IsHeavyTrainingDay is null)
            missing.Add("Criterio/fuente de día pesado no disponible (definición pendiente; no se infiere de carga/1RM/RPE).");

        if (input.HasNearbyMatch == true)
            moments.Add("Partido cercano");
        else if (input.HasNearbyMatch is null)
            missing.Add("No se pudo determinar si hay partido cercano con datos disponibles.");

        if (input.HasInsufficientRest == true)
            moments.Add("Poco descanso");
        else if (input.HasInsufficientRest is null)
            missing.Add("No se pudo determinar descanso reciente (horas/calidad de sueño no disponibles).");

        var goal = (input.AthleticGoal ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(goal))
        {
            missing.Add("Objetivo deportivo no informado.");
            goal = "Objetivo no informado";
        }

        var weightKg = input.WeightKg;
        if (weightKg is null || weightKg <= 0)
            missing.Add("Peso corporal no disponible para cálculo de proteína.");

        // La ficha médica se consulta en Estructura y se clasifica en backend.
        // Ante ausencia de contexto, ficha incompleta, no verificable o riesgo registrado => derivación, sin cantidades.
        var medical = input.MedicalContext;
        if (medical is not null)
        {
            missing.AddRange(medical.MissingData);
            if (medical.RequiresReferral)
                referralReasons.AddRange(medical.ReferralReasons);
        }
        else
        {
            referralReasons.Add("No se pudo verificar la ficha médica del atleta; se requiere derivación profesional antes de indicar pautas nutricionales.");
            missing.Add("Ficha médica no consultada en este flujo.");
        }

        if (RecommendationOutputValidator.TryFindForbiddenWeightLoss(goal, out _))
        {
            // Seguridad: nunca cantidades ni estrategias para bajar de peso.
            return BuildQualitativeOrReferral(
                goal,
                input.IsMinor,
                includeQuantities: false,
                mode: ModeQualitative,
                moments,
                missing,
                referralReasons,
                forceNoWeightLoss: true);
        }

        // Intento de cálculo cuantitativo determinístico (solo casos definidos y seguros).
        if (!input.IsMinor && weightKg is > 0 && TryComputeProteinRange(weightKg.Value, goal, out var minG, out var maxG, out var rangeNote))
        {
            // Cuantitativo permitido solo si no hay motivos de derivación por riesgo médico conocido.
            if (referralReasons.Count == 0)
            {
                return new HabitsEvaluationResult(
                    AthleticGoal: goal,
                    RecommendationMode: ModeQuantitative,
                    MinProteinGrams: minG,
                    MaxProteinGrams: maxG,
                    IncludeQuantities: true,
                    RequiresProfessionalReferral: false,
                    ReferralReasons: [],
                    DetectedMoments: moments,
                    MissingData: missing,
                    PlateGuidance: BuildPlateGuidance(goal, moments, quantitative: true, forceNoWeightLoss: false, referral: false),
                    HydrationAndSleepGuidelines: BuildHydrationAndSleep(moments, input.HasInsufficientRest),
                    CalculationNote: rangeNote);
            }
        }

        // Si no es cuantitativo seguro/posible: cualitativo sin inventar rangos, o derivación si hay riesgos.
        var mode = referralReasons.Count > 0 ? ModeReferral : ModeQualitative;
        var includeQuantities = false; // sin datos/rangos validados o menor/riesgo => no cantidades
        if (!input.IsMinor && referralReasons.Count == 0 && (weightKg is null || weightKg <= 0 || !IsGoalRangeDefined(goal)))
        {
            missing.Add("Rango de proteína no definido/validado para el objetivo o faltan datos; se entrega guía cualitativa sin cantidades (TODO validar rangos g/kg).");
        }

        return BuildQualitativeOrReferral(goal, input.IsMinor, includeQuantities, mode, moments, missing, referralReasons, forceNoWeightLoss: false);
    }

    private static HabitsEvaluationResult BuildQualitativeOrReferral(
        string goal,
        bool isMinor,
        bool includeQuantities,
        string mode,
        List<string> moments,
        List<string> missing,
        List<string> referralReasons,
        bool forceNoWeightLoss)
    {
        if (isMinor)
        {
            missing.Add("Atleta menor de edad: no se indican cantidades de proteína (regla de seguridad).");
            mode = referralReasons.Count > 0 ? ModeReferral : ModeQualitative;
            includeQuantities = false;
        }

        if (forceNoWeightLoss)
        {
            referralReasons.Add("Objetivo orientado a bajar de peso: no se brindan cantidades ni estrategias para perder peso; se sugiere derivación profesional.");
            mode = ModeReferral;
            includeQuantities = false;
        }

        var plate = BuildPlateGuidance(goal, moments, quantitative: false, forceNoWeightLoss, referral: referralReasons.Count > 0);
        var hydrationSleep = BuildHydrationAndSleep(moments, hasInsufficientRest: moments.Contains("Poco descanso"));

        return new HabitsEvaluationResult(
            AthleticGoal: goal,
            RecommendationMode: mode,
            MinProteinGrams: null,
            MaxProteinGrams: null,
            IncludeQuantities: includeQuantities,
            RequiresProfessionalReferral: referralReasons.Count > 0,
            ReferralReasons: referralReasons,
            DetectedMoments: moments,
            MissingData: missing,
            PlateGuidance: plate,
            HydrationAndSleepGuidelines: hydrationSleep,
            CalculationNote: null);
    }

    private static bool TryComputeProteinRange(double weightKg, string goal, out double minG, out double maxG, out string note)
    {
        minG = 0; maxG = 0; note = string.Empty;
        // Único caso definido en el ticket: 70 kg + ganar masa => 100-140 g/día.
        // Generalización determinística asumida solo para "ganar masa/músculo" (~1.43-2.0 g/kg) basada en el ejemplo del ticket.
        // TODO: validar rango g/kg exacto con nutricionista.
        if (IsGainMuscleGoal(goal))
        {
            minG = Math.Round(weightKg * (100.0 / 70.0), 0, MidpointRounding.AwayFromZero);
            maxG = Math.Round(weightKg * (140.0 / 70.0), 0, MidpointRounding.AwayFromZero);
            note = $"Cálculo determinístico (supuesto basado en ejemplo 70 kg => 100-140 g/d): {minG:0}-{maxG:0} g/día para {weightKg:0.#} kg y objetivo de ganar masa. TODO: validar fórmula/rango con profesional.";
            return true;
        }

        // TODO: rangos para otros objetivos pendientes de definición profesional.
        return false;
    }

    private static bool IsGoalRangeDefined(string goal) => IsGainMuscleGoal(goal);

    private static bool IsGainMuscleGoal(string goal)
    {
        var g = goal.ToLowerInvariant();
        return g.Contains("ganar masa") || g.Contains("masa muscular") || g.Contains("hipertrof") || g.Contains("aumentar masa") || g.Contains("ganancia muscular");
    }

    private static string BuildPlateGuidance(string goal, List<string> moments, bool quantitative, bool forceNoWeightLoss, bool referral)
    {
        var parts = new List<string>();
        if (referral)
            parts.Add("Por seguridad, se sugiere derivación a un profesional de nutrición antes de aplicar cambios específicos.");
        if (forceNoWeightLoss)
            parts.Add("No se incluyen estrategias ni pautas de pérdida de peso.");
        else
            parts.Add("Priorizar alimentos habituales y bien tolerados de tu plan, con buena distribución de proteínas y carbohidratos según el entrenamiento.");

        if (moments.Contains("Día pesado de entrenamiento"))
            parts.Add("En día pesado, reforzar energía y proteína en las comidas cercanas al entrenamiento (sin indicar cantidades específicas si no corresponde).");
        if (moments.Contains("Partido cercano"))
            parts.Add("Con partido cercano, priorizar comidas conocidas, fáciles de digerir y buena hidratación previa.");
        if (moments.Contains("Poco descanso"))
            parts.Add("Si hubo poco descanso, sostener comidas regulares y calidad nutricional; evitar cambios bruscos.");

        parts.Add("Priorizar únicamente alimentos reales y bien tolerados; ante dudas, consultar con un profesional.");
        return string.Join(" ", parts);
    }

    private static string BuildHydrationAndSleep(List<string> moments, bool? hasInsufficientRest)
    {
        var parts = new List<string> { "Mantener una hidratación adecuada a lo largo del día, ajustando según calor y carga de entrenamiento." };
        if (moments.Contains("Partido cercano"))
            parts.Add("Aumentar el cuidado de la hidratación en las horas previas al partido.");
        if (hasInsufficientRest == true)
            parts.Add("Priorizar recuperar horas de sueño cuando sea posible, ya que el descanso insuficiente afecta la recuperación.");
        else
            parts.Add("Procurar un descanso suficiente y regular para favorecer la recuperación.");
        return string.Join(" ", parts);
    }

    public static (bool? hasNearbyMatch, bool? hasInsufficientRest) InferMomentsFromData(
        IReadOnlyList<DateOnly>? upcomingMatchDates,
        DateOnly todayArgentina,
        IReadOnlyList<Application.DTOs.Alerts.AthleteCheckInHistoryItemDto>? recentCheckIns)
    {
        bool? hasNearbyMatch = null;
        if (upcomingMatchDates is not null)
        {
            hasNearbyMatch = upcomingMatchDates.Any(d => d >= todayArgentina && d <= todayArgentina.AddDays(MatchNearbyWindowDaysAssumption));
        }

        bool? hasInsufficientRest = null;
        if (recentCheckIns is { Count: > 0 })
        {
            var latest = recentCheckIns
                .OrderByDescending(c => c.Date)
                .FirstOrDefault();
            if (latest is not null)
            {
                // Umbral documentado: ver remarks de la clase (alineado con KAN-477).
                hasInsufficientRest = latest.SleepHours < LowSleepThresholdHoursAssumption;
            }
        }

        // El momento de entrenamiento intenso no se infiere: se obtiene de IHabitsTrainingDayContextProvider (sin fuente hoy).
        return (hasNearbyMatch, hasInsufficientRest);
    }
}

public sealed record HabitsEvaluationInput(
    string? AthleticGoal,
    double? WeightKg,
    bool IsMinor,
    bool? IsHeavyTrainingDay,
    bool? HasNearbyMatch,
    bool? HasInsufficientRest,
    MedicalRecommendationContext? MedicalContext = null,
    IReadOnlyList<string>? ContextNotes = null);

public sealed record HabitsEvaluationResult(
    string AthleticGoal,
    string RecommendationMode,
    double? MinProteinGrams,
    double? MaxProteinGrams,
    bool IncludeQuantities,
    bool RequiresProfessionalReferral,
    IReadOnlyList<string> ReferralReasons,
    IReadOnlyList<string> DetectedMoments,
    IReadOnlyList<string> MissingData,
    string PlateGuidance,
    string HydrationAndSleepGuidelines,
    string? CalculationNote);
