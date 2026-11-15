namespace Application.Common.Recommendations;

using System.Globalization;
using System.Text;
using Application.DTOs.Recommendations;

/// <summary>
/// Constructor centralizado de instrucciones (prompts) y redacción determinística de respaldo
/// para recomendaciones de entrenamiento al PF (perfil frente al grupo) y recomendaciones de hábitos y alimentación (#083 · KAN-378).
/// </summary>
public static class RecommendationPromptBuilder
{
    /// <summary>
    /// Aviso legal obligatorio que acompaña toda recomendación de alimentación (#083 · KAN-378 / KAN-495).
    /// </summary>
    public const string OfficialNutritionDisclaimer =
        "Esto es una recomendación general; para un plan a tu medida, consultá a un nutricionista.";

    /// <summary>
    /// Lista base de alimentos naturales aprobados por defecto cuando el llamador no especifica un subconjunto propio.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultApprovedFoods =
    [
        "pollo",
        "huevo",
        "carne vacuna magra",
        "atún",
        "merluza",
        "yogur natural",
        "queso magro",
        "leche",
        "lentejas",
        "garbanzos",
        "arroz",
        "fideos",
        "papa",
        "batata",
        "avena",
        "pan integral",
        "banana",
        "manzana",
        "naranja",
        "palta",
        "frutos secos",
        "aceite de oliva",
        "tomate",
        "zanahoria",
        "espinaca"
    ];

    /// <summary>
    /// Resuelve la lista efectiva de alimentos aprobados para la solicitud.
    /// Si la solicitud especifica alimentos, se utilizan exclusivamente esos; si es nula, se usa la lista aprobada por defecto.
    /// </summary>
    public static IReadOnlyList<string> ResolveApprovedFoods(IReadOnlyList<string>? requestedFoods)
    {
        if (requestedFoods is null)
            return DefaultApprovedFoods;

        return requestedFoods
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Construye el prompt para que la IA redacte una recomendación de entrenamiento para el PF
    /// basada en los datos del perfil frente al grupo y las tendencias ya calculadas por reglas (#083 · KAN-378).
    /// </summary>
    public static string BuildTrainingRecommendationPrompt(TrainingRecommendationRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? "punto débil detectado frente al promedio del grupo"
            : request.Reason.Trim();

        var sb = new StringBuilder();
        sb.AppendLine("Sos el asistente técnico de un preparador físico en Argentina para la plataforma ProTraining.");
        sb.AppendLine("Tu tarea es ÚNICAMENTE redactar una recomendación de entrenamiento clara y accionable para el preparador físico a partir de los números del perfil del atleta frente a su grupo.");
        sb.AppendLine();
        sb.AppendLine("DATOS CALCULADOS POR REGLAS (NO INVENTAR NI MODIFICAR NÚMEROS):");
        if (!string.IsNullOrWhiteSpace(request.CategoryName))
        {
            sb.AppendLine($"- Categoría / Grupo: {request.CategoryName.Trim()}");
        }
        sb.AppendLine($"- Motivo principal detectado: {reason}");

        if (request.Metrics is { Count: > 0 })
        {
            sb.AppendLine("- Comparación del perfil frente al promedio del grupo:");
            foreach (var metric in request.Metrics)
            {
                var diffText = metric.PercentageDifference.HasValue
                    ? string.Format(CultureInfo.InvariantCulture, " ({0:+0.0;-0.0;0.0}%)", metric.PercentageDifference.Value)
                    : string.Empty;
                var trendText = !string.IsNullOrWhiteSpace(metric.Trend)
                    ? $" | Tendencia: {metric.Trend.Trim()}"
                    : string.Empty;
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  * {0}: marca del atleta {1:0.##} {2} vs promedio del grupo {3:0.##} {2}{4}{5}",
                    metric.Dimension.Trim(),
                    metric.AthleteValue,
                    metric.Unit.Trim(),
                    metric.GroupAverage,
                    diffText,
                    trendText));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.SuggestedFocus))
        {
            sb.AppendLine($"- Enfoque sugerido por reglas: {request.SuggestedFocus.Trim()}");
        }

        sb.AppendLine();
        sb.AppendLine("REGLAS ESTRICTAS DE REDACCIÓN Y SEGURIDAD:");
        sb.AppendLine("1. LA IA SÓLO REDACTA: usá exclusivamente los números y el motivo calculados por reglas detallados arriba. No inventes mediciones, porcentajes ni diagnósticos médicos.");
        sb.AppendLine("2. PRIVACIDAD: no incluyas nombres propios ni datos personales identificables del atleta.");
        sb.AppendLine("3. PROHIBICIONES: no menciones suplementos deportivos (creatina, proteína en polvo, whey, aminoácidos, pre-entrenos, etc.) ni sugerencias o cantidades para bajar de peso / déficit calórico.");
        sb.AppendLine("4. IDIOMA: redactá todos los valores de texto en español rioplatense (Argentina, usando 'vos'), manteniendo las claves del JSON en inglés.");
        sb.AppendLine("5. Respondé ÚNICAMENTE con un objeto JSON válido sin bloques markdown ni texto adicional, respetando este esquema:");
        sb.AppendLine($$"""
            {
              "title": "Título breve de la recomendación para el PF",
              "reason": "{{reason}}",
              "recommendationText": "Explicación clara de la brecha frente al grupo y propuesta práctica de sesiones o énfasis técnico para que el PF evalúe y apruebe."
            }
            """);

        return sb.ToString();
    }

    /// <summary>
    /// Construye el prompt para que la IA redacte una recomendación de alimentación y hábitos
    /// usando exclusivamente los números calculados por reglas y la lista aprobada de alimentos (#083 · KAN-378).
    /// </summary>
    public static string BuildNutritionRecommendationPrompt(NutritionRecommendationRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var goal = string.IsNullOrWhiteSpace(request.AthleticGoal)
            ? "Rendimiento y recuperación deportiva"
            : request.AthleticGoal.Trim();

        var approvedFoods = ResolveApprovedFoods(request.ApprovedFoods);
        var foodsLine = approvedFoods.Count > 0
            ? string.Join(", ", approvedFoods)
            : "ninguno";

        var sb = new StringBuilder();
        sb.AppendLine("Sos el asistente de hábitos saludables y alimentación deportiva de ProTraining en Argentina.");
        sb.AppendLine("Tu tarea es ÚNICAMENTE redactar de forma clara y amigable una recomendación general de alimentación, hidratación y descanso para que el preparador físico la revise.");
        sb.AppendLine();
        sb.AppendLine("PARÁMETROS CALCULADOS POR REGLAS (NO CALCULAR NI INVENTAR NÚMEROS):");
        sb.AppendLine($"- Objetivo deportivo: {goal}");
        if (!string.IsNullOrWhiteSpace(request.Moment))
        {
            sb.AppendLine($"- Momento / Contexto: {request.Moment.Trim()}");
        }

        if (request.IncludeQuantities)
        {
            if (request.MinProteinGrams.HasValue && request.MaxProteinGrams.HasValue)
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "- Proteína diaria calculada por reglas: entre {0:0.#} g y {1:0.#} g por día.",
                    request.MinProteinGrams.Value,
                    request.MaxProteinGrams.Value));
            }
            else if (request.DailyProteinRequirementGrams.HasValue)
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "- Proteína diaria calculada por reglas: {0:0.#} g por día.",
                    request.DailyProteinRequirementGrams.Value));
            }
        }
        else
        {
            sb.AppendLine("- MODALIDAD SIN CANTIDADES (MENOR DE EDAD): NO incluyas gramos, calorías ni cantidades numéricas de comida.");
        }

        if (request.HydrationLiters.HasValue)
        {
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "- Hidratación diaria sugerida por reglas: {0:0.#} litros de agua.",
                request.HydrationLiters.Value));
        }

        if (request.SleepHours.HasValue)
        {
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "- Descanso nocturno sugerido por reglas: {0:0.#} horas de sueño.",
                request.SleepHours.Value));
        }

        sb.AppendLine();
        sb.AppendLine("LISTA CERRADA DE ALIMENTOS APROBADOS (USAR SÓLO ALIMENTOS DE ESTA LISTA):");
        sb.AppendLine($"- Alimentos permitidos: {foodsLine}");
        sb.AppendLine();
        sb.AppendLine("REGLAS ESTRICTAS E INNEGOCIABLES:");
        sb.AppendLine("1. LA IA SÓLO REDACTA: los números vienen calculados por reglas y los alimentos deben salir EXCLUSIVAMENTE de la lista aprobada de arriba.");
        sb.AppendLine("2. SÓLO ALIMENTOS DE LA LISTA: mencioná únicamente alimentos que estén en 'Alimentos permitidos'. Está prohibido agregar cualquier otro alimento fuera de esa lista.");
        sb.AppendLine("3. PROHIBIDO SUPLEMENTOS: jamás menciones suplementos (proteína en polvo, whey protein, creatina, BCAA, aminoácidos, pre-entreno, quemadores, geles, pastillas, batidos proteicos, etc.).");
        sb.AppendLine("4. PROHIBIDO BAJAR DE PESO: jamás menciones ni sugieras bajar de peso, adelgazar, perder kilos, déficit calórico, dieta hipocalórica ni recortar calorías.");
        if (!request.IncludeQuantities)
        {
            sb.AppendLine("5. SIN CANTIDADES EN GRAMOS: por tratarse de una pauta cualitativa / para menores, no escribas gramos ('g', 'gr', 'gramos') ni calorías ('kcal') en el plato.");
        }
        else
        {
            sb.AppendLine("5. RESPETAR NÚMEROS DE REGLAS: si mencionás gramos de proteína, litros de agua u horas de sueño, usá exactamente los valores indicados arriba sin inventar otros.");
        }
        sb.AppendLine("6. PRIVACIDAD: no incluyas nombres de personas.");
        sb.AppendLine("7. IDIOMA Y FORMATO: redactá los textos en español rioplatense (Argentina, usando 'vos') y respondé ÚNICAMENTE con un objeto JSON válido con las claves en inglés:");
        sb.AppendLine("""
            {
              "suggestedPlateComposition": "Descripción clara de cómo armar el plato o la merienda usando únicamente alimentos de la lista aprobada y las pautas de las reglas.",
              "hydrationAndSleepGuidelines": "Consejo de hidratación con agua y horas de descanso nocturno según los datos de las reglas.",
              "mentionedFoods": ["alimento aprobado 1", "alimento aprobado 2"]
            }
            """);

        return sb.ToString();
    }

    /// <summary>
    /// Genera una recomendación de entrenamiento determinística segura cuando los proveedores de IA no están disponibles o fallan la validación (#083 · KAN-378).
    /// </summary>
    public static TrainingRecommendationResponseDto BuildDeterministicTrainingRecommendation(
        TrainingRecommendationRequestDto request,
        string providerUsed = "Reglas")
    {
        ArgumentNullException.ThrowIfNull(request);

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? "rendimiento por debajo del promedio del grupo"
            : request.Reason.Trim();

        var metricsClause = string.Empty;
        if (request.Metrics is { Count: > 0 })
        {
            var metricDescriptions = request.Metrics.Select(m =>
            {
                var diff = m.PercentageDifference.HasValue
                    ? string.Format(CultureInfo.InvariantCulture, " ({0:+0.0;-0.0;0.0}%)", m.PercentageDifference.Value)
                    : string.Empty;
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: {1:0.##} {2} frente a {3:0.##} {2} del grupo{4}",
                    m.Dimension.Trim(),
                    m.AthleteValue,
                    m.Unit.Trim(),
                    m.GroupAverage,
                    diff);
            });

            metricsClause = $" Métricas registradas: {string.Join("; ", metricDescriptions)}.";
        }

        var focusClause = !string.IsNullOrWhiteSpace(request.SuggestedFocus)
            ? $" Se sugiere {request.SuggestedFocus.Trim().TrimEnd('.')}."
            : " Se recomienda reforzar esta capacidad con bloques específicos progresivos dentro de la planificación semanal.";

        var categoryClause = !string.IsNullOrWhiteSpace(request.CategoryName)
            ? $" en {request.CategoryName.Trim()}"
            : string.Empty;

        var title = $"Recomendación de ajuste por {reason}";
        var recommendationText =
            $"Se detectó {reason}{categoryClause}.{metricsClause}{focusClause}";

        return new TrainingRecommendationResponseDto(
            Title: title,
            Reason: reason,
            RecommendationText: recommendationText,
            ProviderUsed: providerUsed);
    }

    /// <summary>
    /// Genera una recomendación de alimentación y hábitos determinística segura usando exclusivamente
    /// los números de las reglas y los alimentos de la lista aprobada (#083 · KAN-378).
    /// </summary>
    public static NutritionRecommendationResponseDto BuildDeterministicNutritionRecommendation(
        NutritionRecommendationRequestDto request,
        string providerUsed = "Reglas")
    {
        ArgumentNullException.ThrowIfNull(request);

        var goal = string.IsNullOrWhiteSpace(request.AthleticGoal)
            ? "rendimiento y recuperación deportiva"
            : request.AthleticGoal.Trim();

        var approvedFoods = ResolveApprovedFoods(request.ApprovedFoods);
        var selectedFoods = approvedFoods.Take(4).ToList();
        var foodsText = selectedFoods.Count > 0
            ? string.Join(", ", selectedFoods)
            : "alimentos frescos habituales";

        var momentPrefix = !string.IsNullOrWhiteSpace(request.Moment)
            ? $"Para el momento '{request.Moment.Trim()}', "
            : string.Empty;

        string proteinClause = string.Empty;
        if (request.IncludeQuantities)
        {
            if (request.MinProteinGrams.HasValue && request.MaxProteinGrams.HasValue)
            {
                proteinClause = string.Format(
                    CultureInfo.InvariantCulture,
                    " Apuntá a cubrir entre {0:0.#} g y {1:0.#} g de proteína diaria distribuidos en las comidas principales.",
                    request.MinProteinGrams.Value,
                    request.MaxProteinGrams.Value);
            }
            else if (request.DailyProteinRequirementGrams.HasValue)
            {
                proteinClause = string.Format(
                    CultureInfo.InvariantCulture,
                    " Apuntá a cubrir unos {0:0.#} g de proteína diaria distribuidos en las comidas principales.",
                    request.DailyProteinRequirementGrams.Value);
            }
        }

        var plateComposition =
            $"{momentPrefix}priorizá armar tus comidas con alimentos naturales de la lista aprobada como {foodsText}, acompañando el objetivo de {goal}.{proteinClause}".Trim();

        var hydrationLiters = request.HydrationLiters ?? 2.5;
        var sleepHours = request.SleepHours ?? 8.0;

        var hydrationAndSleep = string.Format(
            CultureInfo.InvariantCulture,
            "Mantené una hidratación constante con al menos {0:0.#} litros de agua a lo largo del día y procurá descansar {1:0.#} horas de sueño continuo para favorecer la recuperación.",
            hydrationLiters,
            sleepHours);

        return new NutritionRecommendationResponseDto(
            SuggestedPlateComposition: plateComposition,
            HydrationAndSleepGuidelines: hydrationAndSleep,
            MentionedFoods: selectedFoods,
            LegalDisclaimer: OfficialNutritionDisclaimer,
            ProviderUsed: providerUsed);
    }
}
