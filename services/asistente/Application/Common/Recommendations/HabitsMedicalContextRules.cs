namespace Application.Common.Recommendations;

using Application.Common.Helpers;
using Application.Interfaces.Services;

/// <summary>
/// Estado de la verificación médica previa a una recomendación de alimentación (KAN-494).
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
/// Convierte la ficha médica de Estructura en un contexto de seguridad para recomendaciones (KAN-494).
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
/// TODO(KAN-494): dependencia externa. Cuando Estructura publique los campos estructurados, dejar de usar marcadores.
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
        "sin antecedentes",
        "n/a",
        "na"
    ];

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

        var rawClinicalText = $"{record.Pathologies} {record.MedicalHistory} {record.Surgeries}";
        var clinicalText = TextNormalizer.Normalize(rawClinicalText);

        foreach (var negative in ExplicitNegativeMarkers)
            clinicalText = clinicalText.Replace(TextNormalizer.Normalize(negative), " ");

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

        if (string.IsNullOrWhiteSpace(TextNormalizer.Normalize(rawClinicalText)) && !structuredCleared)
        {
            missing.Insert(0, "Ficha médica sin datos clínicos cargados: no se asume ausencia de alergias.");
            return new MedicalRecommendationContext(
                MedicalRecommendationStatus.Incomplete,
                ["La ficha médica está vacía o incompleta; se requiere verificación profesional antes de indicar pautas nutricionales."],
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
}
