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
    /// Etiqueta del grupo de meriendas antes de entrenar (#KAN-495).
    /// </summary>
    public const string PreWorkoutSnackGroupLabel = "Meriendas / Opciones Pre-entreno (Digestión Rápida)";

    /// <summary>
    /// Lista blanca de alimentos organizada formalmente según la taxonomía de nutrición deportiva,
    /// ampliada con opciones más completas sin perder ningún elemento de la lista base original (#KAN-495).
    /// El grupo de meriendas pre-entreno (digestión rápida) referencia alimentos que ya pertenecen
    /// a otros grupos, más una opción combinada propia; es la única suma exclusiva de ese grupo.
    /// </summary>
    public static readonly IReadOnlyList<ApprovedFoodGroup> DefaultApprovedFoodGroups =
    [
        new("Proteínas de Alta Calidad",
        [
            "pollo",
            "carne vacuna magra",
            "atún",
            "merluza",
            "huevo",
            "yogur natural",
            "queso magro",
            "leche",
            "lentejas",
            "garbanzos",
            "ricota magra",
            "pechuga de pavo"
        ]),
        new("Hidratos de Carbono Complejos",
        [
            "arroz",
            "fideos",
            "papa",
            "batata",
            "avena",
            "pan integral",
            "quínoa",
            "galletas de arroz",
            "choclo",
            "calabaza"
        ]),
        new("Frutas y Verduras / Hortalizas",
        [
            "banana",
            "manzana",
            "naranja",
            "pera",
            "frutillas",
            "espinaca",
            "tomate",
            "zanahoria",
            "brócoli",
            "zucchini"
        ]),
        new("Grasas Saludables y Frutos Secos",
        [
            "aceite de oliva",
            "palta",
            "frutos secos",
            "mantequilla de maní",
            "semillas de chía"
        ]),
        new(PreWorkoutSnackGroupLabel,
        [
            "avena",
            "banana",
            "pan integral",
            "yogur natural",
            "frutos secos",
            "manzana",
            "galletas de arroz con mantequilla de maní"
        ])
    ];

    /// <summary>
    /// Lista base de alimentos naturales aprobados por defecto cuando el llamador no especifica un subconjunto propio.
    /// Es la unión aplanada y sin duplicados de <see cref="DefaultApprovedFoodGroups"/> (#KAN-495).
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultApprovedFoods =
        DefaultApprovedFoodGroups
            .SelectMany(group => group.Foods)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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
    /// <summary>
    /// Construye el prompt para que la IA redacte una recomendación de alimentación y hábitos
    /// usando exclusivamente los números calculados por reglas y la lista aprobada de alimentos (#083 · KAN-378, KAN-494).
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

        var quantitiesAllowed = request.IncludeQuantities
            && !request.IsMinor
            && !request.RequiresProfessionalReferral
            && !string.Equals(request.RecommendationMode, HabitsRecommendationRules.ModeReferral, StringComparison.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("Sos el asistente de hábitos saludables y alimentación deportiva de ProTraining en Argentina.");
        sb.AppendLine("Tu tarea es ÚNICAMENTE redactar de forma clara y amigable una recomendación general de alimentación, hidratación y descanso para que el preparador físico la revise.");
        sb.AppendLine();
        sb.AppendLine("PARÁMETROS CALCULADOS POR REGLAS (NO CALCULAR NI INVENTAR NÚMEROS):");
        sb.AppendLine($"- Objetivo deportivo: {goal}");
        if (request.WeightKg.HasValue)
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Peso informado: {0:0.#} kg.", request.WeightKg.Value));
        if (request.IsMinor)
            sb.AppendLine("- Atleta menor de edad: SÍ (no se permiten cantidades de proteína).");
        if (!string.IsNullOrWhiteSpace(request.Moment))
            sb.AppendLine($"- Momento / Contexto: {request.Moment.Trim()}");
        if (request.DetectedMoments is { Count: > 0 })
            sb.AppendLine($"- Momentos detectados: {string.Join("; ", request.DetectedMoments.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct())}");
        if (!string.IsNullOrWhiteSpace(request.RecommendationMode))
            sb.AppendLine($"- Modo de recomendación: {request.RecommendationMode}");
        if (request.RequiresProfessionalReferral)
            sb.AppendLine("- Requiere derivación profesional: SÍ.");
        if (request.ReferralReasons is { Count: > 0 })
            sb.AppendLine($"- Motivos de derivación/seguridad: {string.Join("; ", request.ReferralReasons.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct())}");
        if (request.MissingData is { Count: > 0 })
            sb.AppendLine($"- Datos faltantes / pendientes de validación (TODO): {string.Join("; ", request.MissingData.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct())}");

        if (quantitiesAllowed)
        {
            if (request.MinProteinGrams.HasValue && request.MaxProteinGrams.HasValue)
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "- Proteína diaria calculada por reglas (rango): entre {0:0.#} g y {1:0.#} g por día.",
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
            sb.AppendLine("- MODALIDAD SIN CANTIDADES: NO incluyas gramos, calorías ni cantidades numéricas de comida/proteína (menor, bajar de peso, derivación o datos faltantes).");
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
        if (request.ApprovedFoods is null)
        {
            sb.AppendLine("- Alimentos permitidos por grupo:");
            foreach (var group in DefaultApprovedFoodGroups)
            {
                sb.AppendLine($"  * {group.Label}: {string.Join(", ", group.Foods)}");
            }

            sb.AppendLine($"- CONTEXTO PRE-ENTRENO: si el momento / contexto es antes de entrenar, priorizá una merienda del grupo '{PreWorkoutSnackGroupLabel}'.");
        }
        else
        {
            sb.AppendLine($"- Alimentos permitidos: {foodsLine}");
        }

        sb.AppendLine();
        sb.AppendLine("REGLAS ESTRICTAS E INNEGOCIABLES (KAN-494):");
        sb.AppendLine("1. LA IA SÓLO REDACTA: los números vienen calculados por reglas y los alimentos deben salir EXCLUSIVAMENTE de la lista aprobada de arriba.");
        sb.AppendLine("2. SÓLO ALIMENTOS DE LA LISTA: mencioná únicamente alimentos que estén en 'Alimentos permitidos'. Está prohibido agregar cualquier otro alimento fuera de esa lista.");
        sb.AppendLine("3. PROHIBIDO SUPLEMENTOS: jamás menciones suplementos (proteína en polvo, whey protein, creatina, BCAA, aminoácidos, pre-entreno, quemadores, geles, pastillas, batidos proteicos, etc.).");
        sb.AppendLine("4. PROHIBIDO BAJAR DE PESO: jamás menciones ni sugieras bajar de peso, adelgazar, perder kilos, déficit calórico, dieta hipocalórica ni recortar calorías/cantidades para perder peso.");
        if (!quantitiesAllowed)
        {
            sb.AppendLine("5. SIN CANTIDADES EN GRAMOS NI REFERENCIAS NUMÉRICAS: no escribas gramos ('g', 'gr', 'gramos'), rangos numéricos de proteína, calorías ('kcal') ni cantidades numéricas de litros u horas que no figuren arriba como calculadas por reglas.");
        }
        else
        {
            sb.AppendLine("5. CANTIDADES PERMITIDAS SOLO SI LAS REGLAS LAS INDICAN: si mencionás gramos de proteína, usá exactamente el rango/valor indicado arriba sin inventar otros; lo mismo vale para litros u horas.");
        }
        sb.AppendLine("6. MENORES: si el atleta es menor de edad, no indiques cantidades de proteína.");
        sb.AppendLine("7. DERIVACIÓN/SEGURIDAD: si se indica derivación profesional o faltan datos críticos, sé conservador, no inventes pautas específicas y recomendá consultar a un profesional de nutrición.");
        sb.AppendLine("8. PRIVACIDAD: no incluyas nombres de personas.");
        sb.AppendLine("9. IDIOMA Y FORMATO: redactá los textos en español rioplatense (Argentina, usando 'vos') y respondé ÚNICAMENTE con un objeto JSON válido con las claves en inglés:");
        sb.AppendLine("""
            {
              "suggestedPlateComposition": "Descripción clara y segura usando únicamente alimentos de la lista aprobada, considerando momentos y restricciones.",
              "hydrationAndSleepGuidelines": "Consejo de hidratación y descanso según reglas/contexto.",
              "mentionedFoods": ["alimento aprobado 1", "alimento aprobado 2"],
              "legalDisclaimer": "Aviso legal obligatorio (texto fijo, no modificar)."
            }
            """);
        sb.AppendLine($"10. AVISO LEGAL: el campo 'legalDisclaimer' debe contener exactamente este texto fijo y sin modificar: \"{OfficialNutritionDisclaimer}\"");

        return sb.ToString();
    }
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
    /// <summary>
    /// Genera una recomendación de alimentación y hábitos determinística segura usando exclusivamente
    /// los números de las reglas y los alimentos de la lista aprobada (#083 · KAN-378, KAN-494).
    /// Aplica reglas de seguridad: sin cantidades para menores/bajar de peso/derivación/datos faltantes,
    /// sin suplementos, y manejo explícito de momentos y datos faltantes.
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
        if (selectedFoods.Count == 0)
            selectedFoods = DefaultApprovedFoods.Take(4).ToList();
        var foodsText = string.Join(", ", selectedFoods);

        var referralReasons = request.ReferralReasons?.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList() ?? [];
        var missingData = request.MissingData?.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToList() ?? [];
        var moments = request.DetectedMoments?.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToList() ?? [];

        var quantitiesAllowed = request.IncludeQuantities
            && !request.IsMinor
            && !request.RequiresProfessionalReferral
            && !string.Equals(request.RecommendationMode, HabitsRecommendationRules.ModeReferral, StringComparison.OrdinalIgnoreCase);

        // KAN-494: si es objetivo de bajar de peso, nunca cantidades ni estrategias para perder peso (seguridad).
        var hasWeightLossGoal = RecommendationOutputValidator.TryFindForbiddenWeightLoss(goal, out _);
        if (hasWeightLossGoal)
        {
            quantitiesAllowed = false;
            if (!referralReasons.Any(r => r.Contains("bajar de peso", StringComparison.OrdinalIgnoreCase)))
                referralReasons.Add("Objetivo orientado a bajar de peso: no se brindan cantidades ni estrategias para perder peso; se sugiere derivación profesional.");
        }
        if (request.IsMinor)
        {
            quantitiesAllowed = false;
            if (!missingData.Any(m => m.Contains("menor de edad", StringComparison.OrdinalIgnoreCase)))
                missingData.Add("Atleta menor de edad: no se indican cantidades de proteína (regla de seguridad).");
        }

        string mode = request.RecommendationMode ?? string.Empty;
        if (string.IsNullOrWhiteSpace(mode))
        {
            mode = referralReasons.Count > 0 || hasWeightLossGoal
                ? HabitsRecommendationRules.ModeReferral
                : (quantitiesAllowed && (request.MinProteinGrams.HasValue || request.MaxProteinGrams.HasValue || request.DailyProteinRequirementGrams.HasValue)
                    ? HabitsRecommendationRules.ModeQuantitative
                    : HabitsRecommendationRules.ModeQualitative);
        }
        if (!quantitiesAllowed && string.Equals(mode, HabitsRecommendationRules.ModeQuantitative, StringComparison.OrdinalIgnoreCase))
            mode = referralReasons.Count > 0 ? HabitsRecommendationRules.ModeReferral : HabitsRecommendationRules.ModeQualitative;

        double? minG = quantitiesAllowed ? request.MinProteinGrams : null;
        double? maxG = quantitiesAllowed ? request.MaxProteinGrams : null;
        double? dailyG = quantitiesAllowed ? request.DailyProteinRequirementGrams : null;

        var plateParts = new List<string>();
        if (referralReasons.Count > 0)
        {
            plateParts.Add("Por seguridad, se recomienda consultar a un profesional de nutrición para una pauta personalizada.");
            // KAN-494: con riesgo médico/alergias no verificables, evitar prescripción específica; mantener mención general de alimentos aprobados habituales.
            plateParts.Add($"Mientras tanto, priorizá alimentos habituales y bien tolerados dentro de opciones como {foodsText}, según indicación profesional.");
        }
        else
        {
            var momentPrefix = moments.Count > 0 ? $"Considerando {string.Join(", ", moments).ToLowerInvariant()}, " : string.Empty;
            plateParts.Add($"{momentPrefix}priorizá armar tus comidas con alimentos naturales de la lista aprobada como {foodsText}, alineados al objetivo de {goal}.");
        }

        if (quantitiesAllowed)
        {
            if (minG.HasValue && maxG.HasValue)
                plateParts.Add(string.Format(CultureInfo.InvariantCulture, "Como referencia calculada por reglas, apuntá a un rango diario de proteína de entre {0:0.#} g y {1:0.#} g distribuidos en las comidas.", minG.Value, maxG.Value));
            else if (dailyG.HasValue)
                plateParts.Add(string.Format(CultureInfo.InvariantCulture, "Como referencia calculada por reglas, apuntá a unos {0:0.#} g de proteína diaria distribuidos en las comidas.", dailyG.Value));
        }
        else
        {
            if (request.IsMinor)
                plateParts.Add("Al ser menor de edad, esta recomendación es cualitativa y no incluye cantidades de proteína.");
            if (hasWeightLossGoal)
                plateParts.Add("No se incluyen pautas ni cantidades de pérdida de peso.");
            if (!quantitiesAllowed && !request.IsMinor && !hasWeightLossGoal && (request.WeightKg is null || request.WeightKg <= 0 || missingData.Count > 0))
                plateParts.Add("No se indican cantidades de proteína porque faltan datos o definiciones validadas; se brinda una guía general.");
        }

        if (moments.Any(m => m.Contains("Partido cercano", StringComparison.OrdinalIgnoreCase)))
            plateParts.Add("Con partido cercano, elegí comidas conocidas y fáciles de digerir.");
        if (moments.Any(m => m.Contains("Poco descanso", StringComparison.OrdinalIgnoreCase)))
            plateParts.Add("Si hubo poco descanso, mantené comidas regulares y de buena calidad.");
        if (moments.Any(m => m.Contains("Día pesado", StringComparison.OrdinalIgnoreCase)))
            plateParts.Add("En día pesado, cuidá la energía y la recuperación en las comidas cercanas al entrenamiento.");

        // Seguridad: prohibidos los suplementos (no mencionar términos prohibidos en el texto generado).

        if (missingData.Count > 0)
            plateParts.Add($"Datos pendientes/por validar: {string.Join("; ", missingData)}.");

        var plateComposition = string.Join(" ", plateParts.Where(p => !string.IsNullOrWhiteSpace(p)));

        // KAN-494: no inventar referencias numéricas. Sólo se redactan números que las reglas
        // informaron explícitamente y sólo en modo cuantitativo; en los modos sin cantidades
        // (menor, bajar de peso, derivación o datos faltantes) la guía es cualitativa.
        var hydrationParts = new List<string>();
        if (quantitiesAllowed && request.HydrationLiters is > 0)
        {
            hydrationParts.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Mantené una hidratación constante con agua a lo largo del día (referencia calculada por reglas: {0:0.#} litros, ajustá según calor y carga).",
                request.HydrationLiters.Value));
        }
        else
        {
            hydrationParts.Add("Mantené una hidratación constante con agua a lo largo del día, ajustando según calor y carga de entrenamiento.");
        }

        if (quantitiesAllowed && request.SleepHours is > 0)
        {
            hydrationParts.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Procurá descansar unas {0:0.#} horas de sueño para favorecer la recuperación (referencia calculada por reglas).",
                request.SleepHours.Value));
        }
        else
        {
            hydrationParts.Add("Procurá un descanso suficiente y regular para favorecer la recuperación.");
        }
        if (moments.Any(m => m.Contains("Poco descanso", StringComparison.OrdinalIgnoreCase)))
            hydrationParts.Add("Priorizá recuperar horas de sueño cuando sea posible.");
        if (moments.Any(m => m.Contains("Partido cercano", StringComparison.OrdinalIgnoreCase)))
            hydrationParts.Add("Reforzá la hidratación en las horas previas al partido.");
        var hydrationAndSleep = string.Join(" ", hydrationParts);

        return new NutritionRecommendationResponseDto(
            SuggestedPlateComposition: plateComposition,
            HydrationAndSleepGuidelines: hydrationAndSleep,
            MentionedFoods: selectedFoods,
            LegalDisclaimer: OfficialNutritionDisclaimer,
            ProviderUsed: providerUsed,
            MinProteinGrams: minG,
            MaxProteinGrams: maxG,
            RecommendationMode: mode,
            RequiresProfessionalReferral: referralReasons.Count > 0 || hasWeightLossGoal || request.RequiresProfessionalReferral,
            ReferralReasons: referralReasons,
            DetectedMoments: moments,
            MissingData: missingData);
    }
}

public sealed record ApprovedFoodGroup(string Label, IReadOnlyList<string> Foods);
