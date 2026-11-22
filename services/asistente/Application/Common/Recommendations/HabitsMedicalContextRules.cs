namespace Application.Common.Recommendations;

using System.Text.RegularExpressions;
using Application.Common.Helpers;
using Application.Interfaces.Services;

/// <summary>
/// Estado de la verificación médica previa a una recomendación de alimentación.
/// Solo <see cref="VerifiedNoKnownRisk"/> habilita cantidades; cualquier otro estado deriva a profesional.
/// </summary>
public enum MedicalRecommendationStatus
{
    VerifiedNoKnownRisk,
    RegisteredRisk,
    Incomplete,
    Unavailable
}

/// <summary>
/// Contexto médico derivado íntegramente en backend. No contiene texto clínico: ese dato se usa
/// solo para clasificar el riesgo y nunca se envía a la IA ni se persiste en el snapshot.
/// </summary>
public sealed record MedicalRecommendationContext(
    MedicalRecommendationStatus Status,
    IReadOnlyList<string> ReferralReasons,
    IReadOnlyList<string> MissingData)
{
    public bool RequiresReferral => Status != MedicalRecommendationStatus.VerifiedNoKnownRisk;
}

/// <summary>
/// Convierte la ficha médica de Estructura en un contexto de seguridad para recomendaciones.
/// </summary>
/// <remarks>
/// Estructura registra alergias y condiciones como texto libre (pathologies / medicalHistory / surgeries)
/// y hoy no expone campos estructurados. Por eso una ficha vacía, inexistente o no consultable
/// NUNCA se interpreta como "el atleta no tiene alergias": se marca como incompleta/no disponible y se deriva.
/// Solo una negación explícita registrada ("sin alergias", "sin patologías", etc.) permite modo cuantitativo,
/// manteniendo igualmente una nota de dato no estructurado.
/// Se agregó un seam forward-compatible: si Estructura pasa a enviar hasKnownAllergies / hasKnownConditions,
/// el cliente lo deserializa y esta regla lo prefiere. Un false estructurado NO borra un riesgo detectado en el
/// texto (gana siempre la opción más conservadora); un true deriva directamente.
/// TODO: dependencia externa. Cuando Estructura publique los campos estructurados, dejar de usar marcadores.
/// </remarks>
public static class HabitsMedicalContextRules
{
    public const string MedicalRecordJustification = "Recomendaciones de hábitos y alimentación (KAN-494)";

    private static readonly string[] ExplicitNegativeMarkers =
    [
        "sin alergias",
        "sin alergia",
        "no alergias",
        "no presenta alergias",
        "no refiere alergias",
        "no tiene alergias",
        "ninguna alergia",
        "sin patologias",
        "sin patologia",
        "no presenta patologias",
        "no refiere patologias",
        "sin condiciones medicas",
        "sin antecedentes"
    ];

    // Criterio del PM (22/11, KAN-494): se dan cantidades solo si la ficha dice explícitamente que no hay
    // alergias ni condiciones; ante cualquier condición, o una ficha dudosa, se deriva a un profesional. Una
    // negación nunca anula un riesgo: "Sin alergias. Insuficiencia renal" deriva. Fuentes: ACSM/AND/DC,
    // "Nutrition and Athletic Performance" (Thomas, Erdman y Burke, Med Sci Sports Exerc 2016) para derivar
    // ante condiciones médicas, y KDOQI 2020 (Ikizler et al., Am J Kidney Dis) para la enfermedad renal,
    // que pide restringir la proteína.
    //
    // Las negaciones se buscan como frase completa: antes se borraban como subcadena y "na" se comía
    // "renal" ("re l") y "anafilaxia" ("a filaxia"), y esas fichas recibían gramos de proteína.
    private static readonly Regex[] ExplicitNegativePatterns = ExplicitNegativeMarkers
        .Select(marker => new Regex(
            $@"\b{Regex.Escape(TextNormalizer.Normalize(marker))}\b",
            RegexOptions.CultureInvariant))
        .ToArray();

    // Un campo que solo dice "N/A", "-" o "s/d" no informa nada: cuenta como vacío, nunca como "sin condiciones".
    private static readonly HashSet<string> EmptyFieldPlaceholders = ["na", "n/a", "n/d", "s/d", "-", "--"];

    private static readonly string[] ClinicalRiskMarkers =
    [
        "alerg",
        "patolog",
        "condicion medica",
        "diabet",
        "hipertens",
        "asma",
        "cardio",
        "arritmi",
        "lesion",
        "cirugi",
        "operad",
        "apendic",
        "intoleran",
        "celiac",
        "epilep",
        "renal",
        "hepatic",
        "tiroid",
        "anafil",
        "cancer",
        "tumor",
        "coagul",
        "ulcera",
        "gastritis",
        "hipoglucemi"
    ];

    public static MedicalRecommendationContext Evaluate(
        StructureMedicalRecordContextDto? record,
        DateOnly today)
    {
        if (record is null || record.Availability == StructureMedicalRecordAvailability.Unavailable)
        {
            return new MedicalRecommendationContext(
                MedicalRecommendationStatus.Unavailable,
                ["No se pudo verificar la ficha médica del atleta (consentimiento, permisos o servicio no disponibles); se requiere derivación profesional."],
                ["Ficha médica no verificable: no se puede confirmar ausencia de alergias o condiciones."]);
        }

        if (record.Availability == StructureMedicalRecordAvailability.NoRecord)
        {
            return new MedicalRecommendationContext(
                MedicalRecommendationStatus.Incomplete,
                ["El atleta no tiene ficha médica registrada; no se puede descartar alergias o condiciones sin evaluación profesional."],
                ["Ficha médica no registrada: no se asume ausencia de alergias."]);
        }

        var clinicalText = string.Join(
            " ",
            new[] { record.Pathologies, record.MedicalHistory, record.Surgeries }.Select(NormalizeField)).Trim();
        var hasClinicalText = clinicalText.Length > 0;

        // Se borra solo la frase negada ("sin alergias"), así lo que quede del texto se sigue revisando.
        var hasExplicitNegation = false;
        foreach (var pattern in ExplicitNegativePatterns)
        {
            if (!pattern.IsMatch(clinicalText))
                continue;
            hasExplicitNegation = true;
            clinicalText = pattern.Replace(clinicalText, " ");
        }

        var hasRegisteredRisk = ClinicalRiskMarkers.Any(marker =>
            clinicalText.Contains(TextNormalizer.Normalize(marker), StringComparison.Ordinal));

        var structuredRisk = record.HasKnownAllergies == true || record.HasKnownConditions == true;
        var structuredCleared = record.HasKnownAllergies == false && record.HasKnownConditions == false;

        var missing = new List<string>();
        if (!record.HasPhysicalFitnessCertificate ||
            (record.PhysicalFitnessExpiresAt.HasValue && record.PhysicalFitnessExpiresAt.Value < today))
        {
            missing.Add("Apto físico no vigente o no registrado en la ficha médica.");
        }

        if (hasRegisteredRisk || structuredRisk)
        {
            if (structuredRisk)
                missing.Insert(0, "Estructura registra alergias/condiciones en campos estructurados; requiere evaluación profesional.");

            return new MedicalRecommendationContext(
                MedicalRecommendationStatus.RegisteredRisk,
                ["La ficha médica registra alergias, patologías o antecedentes que requieren evaluación de un profesional de la salud antes de pautar alimentación."],
                missing);
        }

        if (!hasClinicalText && !structuredCleared)
        {
            missing.Insert(0, "Ficha médica sin datos clínicos cargados: no se asume ausencia de alergias.");
            return new MedicalRecommendationContext(
                MedicalRecommendationStatus.Incomplete,
                ["La ficha médica está vacía o incompleta; se requiere verificación profesional antes de indicar pautas nutricionales."],
                missing);
        }

        // Hay texto, pero no dice que no haya alergias ni condiciones (por ejemplo, "juega al fútbol"):
        // no se asume nada y se deriva.
        if (!hasExplicitNegation && !structuredCleared)
        {
            missing.Insert(0, "La ficha médica no indica explícitamente que no haya alergias ni condiciones: no se asume ausencia.");
            return new MedicalRecommendationContext(
                MedicalRecommendationStatus.Incomplete,
                ["La ficha médica no dice explícitamente que no haya alergias ni condiciones; se requiere verificación profesional antes de indicar pautas nutricionales."],
                missing);
        }

        // Negación explícita (texto libre o campos estructurados). Se permite el cálculo con la nota correspondiente.
        missing.Insert(0, structuredCleared
            ? "Alergias/condiciones negadas por campos estructurados de Estructura; confirmar igualmente con el atleta ante cualquier duda."
            : "Alergias/condiciones registradas como texto libre; confirmar con el atleta y el profesional ante cualquier duda.");
        return new MedicalRecommendationContext(
            MedicalRecommendationStatus.VerifiedNoKnownRisk,
            [],
            missing);
    }

    private static string NormalizeField(string? value)
    {
        var normalized = TextNormalizer.Normalize(value);
        return EmptyFieldPlaceholders.Contains(normalized) ? string.Empty : normalized;
    }
}
