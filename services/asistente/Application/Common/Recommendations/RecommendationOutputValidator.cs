namespace Application.Common.Recommendations;

using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Common.Ai;
using Application.Common.Helpers;
using Application.DTOs.Recommendations;

/// <summary>
/// Validador determinístico de esquema JSON y de reglas de negocio/seguridad para las recomendaciones
/// de entrenamiento al PF y de hábitos/alimentación devueltas por la IA (#083 · KAN-378).
/// Garantiza que nunca aparezcan suplementos ni cantidades para bajar de peso y que en alimentación
/// se utilicen únicamente alimentos de la lista aprobada.
/// </summary>
public static class RecommendationOutputValidator
{
    private static readonly string[] ForbiddenSupplementTerms =
    [
        "suplemento",
        "suplementacion",
        "whey",
        "proteina en polvo",
        "polvo proteico",
        "batido proteico",
        "batido de proteina",
        "aislado de suero",
        "suero de leche",
        "creatina",
        "bcaa",
        "aminoacido",
        "glutamina",
        "pre entreno",
        "pre-entreno",
        "preworkout",
        "pre workout",
        "quemador",
        "termogenico",
        "ganador de peso",
        "mass gainer",
        "gainer",
        "caseina",
        "maltodextrina",
        "dextrosa",
        "gel energetico",
        "barra proteica",
        "barrita proteica",
        "capsula",
        "pastilla",
        "comprimido",
        "multivitaminico",
        "anabolico",
        "esteroide",
        "carnitina",
        "l-carnitina",
        "oxido nitrico",
        "beta alanina",
        "beta-alanina",
        "cafeina anhidra",
        "scoop"
    ];

    private static readonly string[] ForbiddenWeightLossTerms =
    [
        "bajar de peso",
        "perder peso",
        "descenso de peso",
        "reduccion de peso",
        "adelgazar",
        "deficit calorico",
        "en deficit",
        "hipocalorica",
        "dieta hipocalorica",
        "restringir calorias",
        "restriccion calorica",
        "quemar grasa",
        "perdida de grasa",
        "bajar grasa",
        "reducir grasa",
        "menos calorias",
        "recorte calorico",
        "recortar calorias",
        "dieta restrictiva",
        "ayuno intermitente",
        "ayunar",
        "perder kilos",
        "bajar kilos",
        "para bajar",
        "para adelgazar",
        "comer menos",
        "comas menos",
        "come menos",
        "ingerir menos",
        "menos comida",
        "reducir porciones",
        "reduci porciones",
        "reducir las porciones",
        "reduci las porciones",
        "achicar porciones",
        "achica porciones",
        "achicar las porciones",
        "disminuir porciones",
        "disminui porciones",
        "limitar porciones",
        "recortar porciones",
        "porciones mas chicas",
        "porciones reducidas",
        "bajar la ingesta",
        "reducir la ingesta",
        "restringir la ingesta",
        "saltar comidas",
        "saltarse comidas",
        "saltear comidas",
        "saltearse comidas",
        "evitar cenar",
        "no cenar"
    ];

    /// <summary>
    /// Detecta variaciones verbales y expresiones indirectas de restricción alimentaria o descenso de peso (KAN-378).
    /// </summary>
    private static readonly Regex ForbiddenWeightLossRegex = new(
        @"\b(" +
        @"(?:comer|comas|come|ingerir|consumir)\s+(?:mucho\s+)?menos" +
        @"|(?:reducir|reduci|reduzca|disminuir|disminui|achicar|achica|recortar|recorta|limitar|limita|bajar|baja|restringir|restringi)\s+(?:las?\s+|tus?\s+|sus?\s+)?(?:porcion(?:es)?|racion(?:es)?|comida(?:s)?|ingesta|caloria(?:s)?|cantidad(?:es)?\s+de\s+comida)" +
        @"|(?:saltar|saltear|evitar|suprimir|omitir)(?:se|te)?\s+(?:el\s+|la\s+|las?\s+|los?\s+)?(?:desayuno|almuerzo|merienda|cena|comida(?:s)?)" +
        @"|(?:bajar|perder|reducir|disminuir)\s+(?:el\s+|tu\s+|su\s+|de\s+)?(?:peso|grasa|kilo(?:s)?)" +
        @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Catálogo ampliado de alimentos conocidos (capa 1) que complementa la validación por lista blanca
    /// de slots alimentarios en el texto libre (capa 2) (#083 · KAN-378).
    /// </summary>
    private static readonly string[] KnownFoodCatalog =
    [
        "pollo",
        "carne",
        "vaca",
        "cerdo",
        "cordero",
        "pavo",
        "pescado",
        "salmon",
        "atun",
        "merluza",
        "sardina",
        "caballa",
        "trucha",
        "marisco",
        "camaron",
        "langostino",
        "huevo",
        "leche",
        "yogur",
        "queso",
        "ricota",
        "manteca",
        "crema",
        "arroz",
        "fideo",
        "pasta",
        "tallarin",
        "noqui",
        "raviol",
        "sorrentino",
        "lasana",
        "canelon",
        "papa",
        "papa frita",
        "papas fritas",
        "batata",
        "boniato",
        "mandioca",
        "choclo",
        "polenta",
        "avena",
        "quinoa",
        "cebada",
        "centeno",
        "pan",
        "tostada",
        "tostado",
        "sanguche",
        "sandwich",
        "pebete",
        "galleta",
        "galletita",
        "fajita",
        "rapidita",
        "tarta",
        "lenteja",
        "garbanzo",
        "poroto",
        "arveja",
        "soja",
        "tofu",
        "seitan",
        "tempeh",
        "banana",
        "manzana",
        "naranja",
        "mandarina",
        "pera",
        "durazno",
        "frutilla",
        "arandano",
        "frambuesa",
        "uva",
        "kiwi",
        "anana",
        "pina",
        "sandia",
        "melon",
        "pomelo",
        "ciruela",
        "higo",
        "mango",
        "palta",
        "aguacate",
        "tomate",
        "lechuga",
        "espinaca",
        "acelga",
        "brocoli",
        "coliflor",
        "zanahoria",
        "zapallo",
        "calabaza",
        "zapallito",
        "zucchini",
        "berenjena",
        "cebolla",
        "morron",
        "pepino",
        "remolacha",
        "rucula",
        "repollo",
        "esparrago",
        "champinon",
        "hongo",
        "nuez",
        "nueces",
        "almendra",
        "mani",
        "castana",
        "avellana",
        "pistacho",
        "chia",
        "lino",
        "sesamo",
        "girasol",
        "aceite",
        "miel",
        "mermelada",
        "dulce de leche",
        "azucar",
        "chocolate",
        "cacao",
        "helado",
        "factura",
        "medialuna",
        "bizcocho",
        "chipa",
        "alfajor",
        "churro",
        "torta",
        "budin",
        "flan",
        "postre",
        "empanada",
        "pizza",
        "hamburguesa",
        "paty",
        "pancho",
        "milanesa",
        "nugget",
        "salchicha",
        "salchichon",
        "salame",
        "bondiola",
        "matambre",
        "asado",
        "vacio",
        "entrana",
        "costilla",
        "chorizo",
        "choripan",
        "morcilla",
        "mortadela",
        "longaniza",
        "embutido",
        "achura",
        "molleja",
        "fiambre",
        "jamon",
        "panceta",
        "tocino",
        "locro",
        "guiso",
        "estofado",
        "tortilla",
        "fritura",
        "snack",
        "chizito",
        "pochoclo",
        "sushi",
        "taco",
        "burrito",
        "kebab",
        "shawarma",
        "mayonesa",
        "ketchup",
        "mostaza",
        "salsa",
        "margarina",
        "gaseosa",
        "jugo",
        "alcohol",
        "cerveza",
        "vino",
        "fernet",
        "energizante"
    ];

    private const string ApprovedFoodSentinel = "__approved_food__";

    // Detecta candidatos que aparecen en posiciones sintácticas de enumeración de alimentos (capa 2: lista blanca).
    // Usa lookbehind para no consumir el token aprobado previo y capturar cadenas como "pollo con arroz y burrata".
    private static readonly Regex FoodSlotCueRegex = new(
        @"(?:\b(?:combinando|combina|combinar|como|con|come|comer|suma|sumar|agrega|agregar|inclui|incluir|elegi|elegir|consumi|consumir|incorpora|incorporar|acompanado\s+de|acompanada\s+de|acompanar\s+con|plato\s+con|plato\s+de|porcion\s+de|porciones\s+de|poco\s+de)\s+|" +
        @"(?<=" + ApprovedFoodSentinel + @")(?:\s*,\s*|\s+(?:y|o|e|u)\s+))" +
        @"(?:(?:un|una|unos|unas|el|la|los|las|tu|tus|su|sus|al|del|de|mas|algo\s+de|poco\s+de)\s+)*" +
        @"([a-z_][a-z0-9_]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Vocabulario estructural no alimentario permitido dentro de slots de redacción del plato (capa 2: lista blanca).
    /// Cualquier sustantivo que aparezca en posición de alimento y no esté en los alimentos aprobados ni aquí es rechazado.
    /// </summary>
    private static readonly HashSet<string> AllowedNonFoodSlotTokens = new(StringComparer.Ordinal)
    {
        ApprovedFoodSentinel,
        // Conectores, artículos, preposiciones y adverbios
        "a", "al", "algo", "algun", "alguna", "algunas", "algunos", "antes", "asi", "bien",
        "cada", "casi", "como", "con", "contra", "cualquier", "cuando", "de", "del", "desde",
        "despues", "durante", "e", "el", "ella", "en", "entre", "esa", "esas", "ese", "esos",
        "esta", "estas", "este", "estos", "hacia", "hasta", "la", "las", "lo", "los", "luego",
        "mas", "menos", "mientras", "misma", "mismo", "mucha", "muchas", "mucho", "muchos",
        "muy", "ni", "no", "o", "otra", "otras", "otro", "otros", "para", "pero", "poca",
        "pocas", "poco", "pocos", "por", "porque", "que", "se", "segun", "si", "siempre",
        "sin", "sobre", "solo", "su", "sus", "tambien", "tan", "tanto", "toda", "todas",
        "todo", "todos", "tras", "tu", "tus", "u", "un", "una", "unas", "uno", "unos", "y", "ya",
        // Verbos frecuentes en recomendaciones de hábitos/plato
        "acompanando", "acompanar", "acompanado", "acompanada", "acompanalo", "acompanala",
        "activar", "adaptar", "agregar", "agrega", "agregando", "ajustar", "alimentar",
        "alternar", "alterna", "apoyar", "aportar", "aporta", "aprovechar", "apuntar", "apunta",
        "armar", "arma", "armando", "asegurar", "asegura", "ayudar", "beber", "bebe",
        "buscar", "busca", "combinar", "combina", "combinando", "comer", "come", "comiendo",
        "completar", "completa", "consumir", "consumi", "contar", "cubrir", "cuidar",
        "descansar", "descansa", "distribuir", "distribui", "distribuidos", "distribuidas",
        "dormir", "dormi", "elegir", "elegi", "empezar", "entrenar", "equilibrar", "estar",
        "favorecer", "favorece", "hacer", "hidratar", "hidratate", "incluir", "inclui",
        "incluyendo", "incorporar", "incorpora", "incorporando", "llegar", "mantener", "mantene",
        "manteniendo", "mejorar", "mejora", "mezclar", "mezcla", "obtener", "optimizar",
        "organizar", "organiza", "planificar", "poder", "podes", "preparar", "prepara",
        "priorizar", "prioriza", "procurar", "procura", "recordar", "recorda", "recuperar",
        "reforzar", "repartir", "reparti", "reponer", "repone", "respetar", "seguir", "segui",
        "servir", "sostener", "sumar", "suma", "sumando", "tener", "tomar", "toma", "tratar",
        "usar", "usa", "usando", "utilizar", "utiliza", "variar", "varia",
        // Sustantivos y adjetivos nutricionales/deportivos generales (no son alimentos concretos)
        "agua", "alimento", "alimentos", "almuerzo", "almuerzos", "alta", "alto", "altas", "altos",
        "aporte", "aportes", "aprobada", "aprobadas", "aprobado", "aprobados", "atleta",
        "balance", "balanceada", "balanceado", "base", "basica", "basico", "buen", "buena",
        "buenas", "bueno", "buenos", "calidad", "cantidad", "cantidades", "carbohidrato",
        "carbohidratos", "cena", "cenas", "ciclo", "colacion", "colaciones", "combinacion",
        "combinaciones", "comida", "comidas", "competencia", "completa", "completo", "compleja",
        "complejas", "complejo", "complejos", "constante", "continuo", "continua", "cuerpo",
        "deporte", "deportiva", "deportivo", "desayuno", "desayunos", "descanso", "dia", "dias",
        "diaria", "diarias", "diario", "diarios", "digestion", "energia", "energetica",
        "energetico", "entrenamiento", "entrenamientos", "equilibrada", "equilibrado",
        "equilibrio", "esfuerzo", "estilo", "exigencia", "fatiga", "fibra", "fibras", "fisica",
        "fisico", "forma", "fresca", "frescas", "fresco", "frescos", "fruta", "frutas",
        "fuente", "fuentes", "fuerza", "g", "gr", "gramo", "gramos", "grasa", "grasas",
        "habito", "habitos", "hidratacion", "hidrato", "hidratos", "hora", "horas", "hortaliza",
        "hortalizas", "ideal", "ingesta", "intensa", "intenso", "intensidad", "jornada",
        "kcal", "kg", "l", "ligera", "ligero", "lista", "litro", "litros", "liviana", "liviano",
        "magra", "magro", "manana", "masa", "mejor", "mejores", "menu", "merienda", "meriendas",
        "mezcla", "mg", "min", "mineral", "minerales", "minuto", "minutos", "mitad", "ml",
        "moderada", "moderado", "momento", "momentos", "muscular", "musculo", "musculos",
        "natural", "naturales", "nivel", "noche", "nutricion", "nutricional", "nutriente",
        "nutrientes", "objetivo", "objetivos", "opcion", "opciones", "parte", "partido",
        "partidos", "pauta", "pautas", "pesada", "pesado", "plan", "plato", "platos", "porcion",
        "porciones", "post", "practica", "practicas", "pre", "principal", "principales",
        "proteica", "proteico", "proteina", "proteinas", "racion", "raciones", "rapida",
        "rapido", "recomendacion", "recuperacion", "regular", "rendimiento", "reposo", "ritmo",
        "rutina", "salud", "saludable", "saludables", "semana", "semanal", "sencilla",
        "sencillo", "sesion", "sesiones", "simple", "simples", "suave", "sueno", "suficiente",
        "tarde", "tiempo", "tipo", "trabajo", "variada", "variado", "variedad", "vegetal",
        "vegetales", "verdura", "verduras", "vitamina", "vitaminas", "volumen"
    };

    private static readonly Regex QuantityRegex = new(
        @"\b\d+([.,]\d+)?\s*(g|gr|grs|gramos|kcal|calorias|kg|mg)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Valida la respuesta del modelo para una recomendación de entrenamiento al PF (perfil frente al grupo) (#083 · KAN-378).
    /// </summary>
    public static RecommendationValidationResult<TrainingRecommendationResponseDto> ValidateTrainingOutput(
        string? rawResponse,
        TrainingRecommendationRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return RecommendationValidationResult<TrainingRecommendationResponseDto>.Failure(
                "RespuestaVacia",
                "El texto recibido del modelo de IA está vacío.");
        }

        var cleanedJson = AiRoutineParser.CleanMarkdownFences(rawResponse);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(cleanedJson);
        }
        catch (JsonException ex)
        {
            return RecommendationValidationResult<TrainingRecommendationResponseDto>.Failure(
                "JsonInvalido",
                $"Sintaxis JSON inválida devuelta por la IA: {ex.Message}",
                isSyntaxError: true);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return RecommendationValidationResult<TrainingRecommendationResponseDto>.Failure(
                    "SchemaInvalido",
                    "El payload devuelto por la IA debe ser un objeto JSON.");
            }

            var title = TryGetStringProperty(root, "title", "titulo");
            if (string.IsNullOrWhiteSpace(title))
            {
                return RecommendationValidationResult<TrainingRecommendationResponseDto>.Failure(
                    "SchemaInvalido",
                    "Falta el campo obligatorio 'title' en la recomendación de entrenamiento.");
            }

            var parsedReason = TryGetStringProperty(root, "reason", "motivo");
            var effectiveReason = !string.IsNullOrWhiteSpace(request.Reason)
                ? request.Reason.Trim()
                : parsedReason;

            if (string.IsNullOrWhiteSpace(effectiveReason))
            {
                return RecommendationValidationResult<TrainingRecommendationResponseDto>.Failure(
                    "SchemaInvalido",
                    "Falta el campo obligatorio 'reason' en la recomendación de entrenamiento.");
            }

            var recommendationText = TryGetStringProperty(root, "recommendationText", "recommendation", "recomendacion");
            if (string.IsNullOrWhiteSpace(recommendationText))
            {
                return RecommendationValidationResult<TrainingRecommendationResponseDto>.Failure(
                    "SchemaInvalido",
                    "Falta el campo obligatorio 'recommendationText' en la recomendación de entrenamiento.");
            }

            return ValidateTrainingDto(
                new TrainingRecommendationResponseDto(
                    Title: title.Trim(),
                    Reason: effectiveReason.Trim(),
                    RecommendationText: recommendationText.Trim()),
                request);
        }
    }

    /// <summary>
    /// Valida un DTO de recomendación de entrenamiento ya instanciado contra las reglas de negocio y seguridad (#083 · KAN-378).
    /// </summary>
    public static RecommendationValidationResult<TrainingRecommendationResponseDto> ValidateTrainingDto(
        TrainingRecommendationResponseDto? dto,
        TrainingRecommendationRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (dto is null)
        {
            return RecommendationValidationResult<TrainingRecommendationResponseDto>.Failure(
                "RespuestaVacia",
                "La recomendación de entrenamiento está vacía.");
        }

        if (string.IsNullOrWhiteSpace(dto.Title))
        {
            return RecommendationValidationResult<TrainingRecommendationResponseDto>.Failure(
                "SchemaInvalido",
                "Falta el campo obligatorio 'title' en la recomendación de entrenamiento.");
        }

        var effectiveReason = !string.IsNullOrWhiteSpace(request.Reason)
            ? request.Reason.Trim()
            : dto.Reason?.Trim();

        if (string.IsNullOrWhiteSpace(effectiveReason))
        {
            return RecommendationValidationResult<TrainingRecommendationResponseDto>.Failure(
                "SchemaInvalido",
                "Falta el campo obligatorio 'reason' en la recomendación de entrenamiento.");
        }

        if (string.IsNullOrWhiteSpace(dto.RecommendationText))
        {
            return RecommendationValidationResult<TrainingRecommendationResponseDto>.Failure(
                "SchemaInvalido",
                "Falta el campo obligatorio 'recommendationText' en la recomendación de entrenamiento.");
        }

        var combinedText = $"{dto.Title} {dto.Reason} {dto.RecommendationText}";

        if (TryFindForbiddenSupplement(combinedText, out var supplementFound))
        {
            return RecommendationValidationResult<TrainingRecommendationResponseDto>.Failure(
                "SuplementoDetectado",
                $"La recomendación de entrenamiento menciona suplementos prohibidos ('{supplementFound}').");
        }

        if (TryFindForbiddenWeightLoss(combinedText, out var weightLossFound))
        {
            return RecommendationValidationResult<TrainingRecommendationResponseDto>.Failure(
                "BajarDePesoDetectado",
                $"La recomendación de entrenamiento incluye pautas o cantidades para bajar de peso ('{weightLossFound}').");
        }

        return RecommendationValidationResult<TrainingRecommendationResponseDto>.Success(
            new TrainingRecommendationResponseDto(
                Title: dto.Title.Trim(),
                Reason: effectiveReason,
                RecommendationText: dto.RecommendationText.Trim(),
                ProviderUsed: string.IsNullOrWhiteSpace(dto.ProviderUsed) ? "Reglas" : dto.ProviderUsed));
    }

    /// <summary>
    /// Valida la respuesta del modelo para una recomendación de hábitos y alimentación (#083 · KAN-378).
    /// Verifica que no mencione suplementos, que no incluya cantidades ni pautas para bajar de peso,
    /// y que utilice únicamente alimentos presentes en la lista aprobada.
    /// </summary>
    public static RecommendationValidationResult<NutritionRecommendationResponseDto> ValidateNutritionOutput(
        string? rawResponse,
        NutritionRecommendationRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (TryFindForbiddenWeightLoss(request.AthleticGoal, out var goalWeightLoss))
        {
            return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                "BajarDePesoDetectado",
                $"El objetivo solicitado incluye pautas prohibidas para bajar de peso ('{goalWeightLoss}').");
        }

        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                "RespuestaVacia",
                "El texto recibido del modelo de IA está vacío.");
        }

        var cleanedJson = AiRoutineParser.CleanMarkdownFences(rawResponse);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(cleanedJson);
        }
        catch (JsonException ex)
        {
            return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                "JsonInvalido",
                $"Sintaxis JSON inválida devuelta por la IA: {ex.Message}",
                isSyntaxError: true);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                    "SchemaInvalido",
                    "El payload devuelto por la IA debe ser un objeto JSON.");
            }

            var plateComposition = TryGetStringProperty(
                root,
                "suggestedPlateComposition",
                "plateComposition",
                "composicionPlato");

            if (string.IsNullOrWhiteSpace(plateComposition))
            {
                return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                    "SchemaInvalido",
                    "Falta el campo obligatorio 'suggestedPlateComposition' en la recomendación de alimentación.");
            }

            var hydrationAndSleep = TryGetStringProperty(
                root,
                "hydrationAndSleepGuidelines",
                "hydrationAndSleep",
                "pautasHidratacionYDescanso");

            if (string.IsNullOrWhiteSpace(hydrationAndSleep))
            {
                return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                    "SchemaInvalido",
                    "Falta el campo obligatorio 'hydrationAndSleepGuidelines' en la recomendación de alimentación.");
            }

            var declaredFoods = new List<string>();
            if (TryGetPropertyCaseInsensitive(root, "mentionedFoods", out var foodsProp) ||
                TryGetPropertyCaseInsensitive(root, "alimentosMencionados", out foodsProp) ||
                TryGetPropertyCaseInsensitive(root, "foods", out foodsProp))
            {
                if (foodsProp.ValueKind != JsonValueKind.Array)
                {
                    return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                        "SchemaInvalido",
                        "El campo 'mentionedFoods' debe ser un arreglo JSON de cadenas de texto.");
                }

                foreach (var item in foodsProp.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                    {
                        return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                            "SchemaInvalido",
                            "Todos los elementos de 'mentionedFoods' deben ser nombres de alimentos válidos.");
                    }

                    declaredFoods.Add(item.GetString()!.Trim());
                }
            }

            return ValidateNutritionDto(
                new NutritionRecommendationResponseDto(
                    SuggestedPlateComposition: plateComposition.Trim(),
                    HydrationAndSleepGuidelines: hydrationAndSleep.Trim(),
                    MentionedFoods: declaredFoods,
                    LegalDisclaimer: RecommendationPromptBuilder.OfficialNutritionDisclaimer),
                request);
        }
    }

    /// <summary>
    /// Valida un DTO de recomendación de alimentación ya instanciado contra las reglas de negocio y seguridad (#083 · KAN-378).
    /// </summary>
    public static RecommendationValidationResult<NutritionRecommendationResponseDto> ValidateNutritionDto(
        NutritionRecommendationResponseDto? dto,
        NutritionRecommendationRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (TryFindForbiddenWeightLoss(request.AthleticGoal, out var goalWeightLoss))
        {
            return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                "BajarDePesoDetectado",
                $"El objetivo solicitado incluye pautas prohibidas para bajar de peso ('{goalWeightLoss}').");
        }

        if (dto is null)
        {
            return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                "RespuestaVacia",
                "La recomendación de alimentación está vacía.");
        }

        if (string.IsNullOrWhiteSpace(dto.SuggestedPlateComposition))
        {
            return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                "SchemaInvalido",
                "Falta el campo obligatorio 'suggestedPlateComposition' en la recomendación de alimentación.");
        }

        if (string.IsNullOrWhiteSpace(dto.HydrationAndSleepGuidelines))
        {
            return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                "SchemaInvalido",
                "Falta el campo obligatorio 'hydrationAndSleepGuidelines' en la recomendación de alimentación.");
        }

        var plateComposition = dto.SuggestedPlateComposition.Trim();
        var hydrationAndSleep = dto.HydrationAndSleepGuidelines.Trim();
        var declaredFoods = dto.MentionedFoods?
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .ToList() ?? [];

        var combinedText = $"{plateComposition} {hydrationAndSleep} {string.Join(" ", declaredFoods)}";

        // 1. Validar que NO aparezcan suplementos
        if (TryFindForbiddenSupplement(combinedText, out var supplementFound))
        {
            return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                "SuplementoDetectado",
                $"La recomendación de alimentación menciona suplementos prohibidos ('{supplementFound}').");
        }

        // 2. Validar que NO aparezcan pautas ni cantidades para bajar de peso
        if (TryFindForbiddenWeightLoss(combinedText, out var weightLossFound))
        {
            return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                "BajarDePesoDetectado",
                $"La recomendación de alimentación incluye pautas o cantidades para bajar de peso ('{weightLossFound}').");
        }

        // 3. Validar que si no se permiten cantidades (ej. menores de edad), no aparezcan gramos ni calorías
        if (!request.IncludeQuantities)
        {
            var normalizedPlate = TextNormalizer.Normalize(plateComposition);
            if (QuantityRegex.IsMatch(normalizedPlate))
            {
                return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                    "CantidadesNoPermitidas",
                    "La recomendación incluye cantidades en gramos o calorías cuando se indicó modalidad sin cantidades.");
            }
        }

        // 4. Validar que se utilicen ÚNICAMENTE alimentos de la lista aprobada
        var approvedFoods = RecommendationPromptBuilder.ResolveApprovedFoods(request.ApprovedFoods);
        if (approvedFoods.Count == 0)
        {
            return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                "AlimentoNoAprobado",
                "La lista de alimentos aprobados está vacía; no se puede validar la recomendación.");
        }

        // 4a. Cada alimento declarado en mentionedFoods debe pertenecer a la lista aprobada
        foreach (var declared in declaredFoods)
        {
            if (!MatchesAnyApprovedFood(declared, approvedFoods))
            {
                return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                    "AlimentoNoAprobado",
                    $"El alimento '{declared}' no pertenece a la lista de alimentos aprobados.");
            }
        }

        // 4b. Escanear el texto redactado y los alimentos declarados para asegurar que no mencionen alimentos fuera de la lista aprobada
        var foodScanText = $"{plateComposition} {hydrationAndSleep} {string.Join(" ", declaredFoods)}";
        if (TryFindUnapprovedFoodInText(foodScanText, approvedFoods, out var unapprovedFood))
        {
            return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                "AlimentoNoAprobado",
                $"La recomendación menciona un alimento fuera de la lista aprobada ('{unapprovedFood}').");
        }

        // 4b-bis. Capa 2 (lista blanca sobre slots alimentarios del plato): atrapa incluso alimentos
        // que no figuren en KnownFoodCatalog (ej. comidas regionales o no listadas)
        if (TryFindUnapprovedFoodInPlateSlots(plateComposition, approvedFoods, request, out var unapprovedSlotFood))
        {
            return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                "AlimentoNoAprobado",
                $"La recomendación menciona un alimento fuera de la lista aprobada ('{unapprovedSlotFood}').");
        }

        // 4c. Verificar que mencione al menos un alimento de la lista aprobada
        var matchedApprovedFoods = ExtractMatchedApprovedFoods(foodScanText, declaredFoods, approvedFoods);
        if (matchedApprovedFoods.Count == 0)
        {
            return RecommendationValidationResult<NutritionRecommendationResponseDto>.Failure(
                "AlimentoNoAprobado",
                "La recomendación no utiliza ningún alimento de la lista aprobada.");
        }

        return RecommendationValidationResult<NutritionRecommendationResponseDto>.Success(
            new NutritionRecommendationResponseDto(
                SuggestedPlateComposition: plateComposition,
                HydrationAndSleepGuidelines: hydrationAndSleep,
                MentionedFoods: matchedApprovedFoods,
                LegalDisclaimer: RecommendationPromptBuilder.OfficialNutritionDisclaimer,
                ProviderUsed: string.IsNullOrWhiteSpace(dto.ProviderUsed) ? "Reglas" : dto.ProviderUsed));
    }

    public static bool TryFindForbiddenSupplement(string? text, out string matchedTerm)
    {
        matchedTerm = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = TextNormalizer.Normalize(text);
        foreach (var term in ForbiddenSupplementTerms)
        {
            var normTerm = TextNormalizer.Normalize(term);
            if (ContainsWholePhraseOrPlural(normalized, normTerm))
            {
                matchedTerm = term;
                return true;
            }
        }

        return false;
    }

    public static bool TryFindForbiddenWeightLoss(string? text, out string matchedTerm)
    {
        matchedTerm = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = TextNormalizer.Normalize(text);
        foreach (var term in ForbiddenWeightLossTerms)
        {
            var normTerm = TextNormalizer.Normalize(term);
            if (ContainsWholePhraseOrPlural(normalized, normTerm))
            {
                matchedTerm = term;
                return true;
            }
        }

        var regexMatch = ForbiddenWeightLossRegex.Match(normalized);
        if (regexMatch.Success)
        {
            matchedTerm = regexMatch.Value;
            return true;
        }

        return false;
    }

    private static bool MatchesAnyApprovedFood(string candidateFood, IReadOnlyList<string> approvedFoods)
    {
        var normCandidate = TextNormalizer.Normalize(candidateFood);
        if (string.IsNullOrEmpty(normCandidate))
            return false;

        foreach (var approved in approvedFoods)
        {
            var normApproved = TextNormalizer.Normalize(approved);
            if (string.IsNullOrEmpty(normApproved))
                continue;

            if (normCandidate == normApproved ||
                normCandidate == normApproved + "s" ||
                normCandidate == normApproved + "es" ||
                normApproved == normCandidate + "s" ||
                normApproved == normCandidate + "es" ||
                ContainsWholePhraseOrPlural(normCandidate, normApproved))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindUnapprovedFoodInText(
        string text,
        IReadOnlyList<string> approvedFoods,
        out string unapprovedFood)
    {
        unapprovedFood = string.Empty;
        var normalizedText = TextNormalizer.Normalize(text);
        if (string.IsNullOrEmpty(normalizedText))
            return false;

        // Enmascarar primero las frases de alimentos aprobados (de más largas a más cortas)
        // para que alimentos compuestos aprobados (ej. "pan integral", "queso magro") no disparen falsos positivos
        var maskedText = normalizedText;
        foreach (var approved in approvedFoods
                     .Select(TextNormalizer.Normalize)
                     .Where(a => !string.IsNullOrEmpty(a))
                     .OrderByDescending(a => a.Length))
        {
            maskedText = MaskWholePhraseAndPlurals(maskedText, approved);
        }

        var normalizedApprovedTokens = approvedFoods
            .Select(TextNormalizer.Normalize)
            .Where(a => !string.IsNullOrEmpty(a))
            .ToList();

        foreach (var knownFood in KnownFoodCatalog)
        {
            var normKnown = TextNormalizer.Normalize(knownFood);
            if (string.IsNullOrEmpty(normKnown))
                continue;

            // Si el alimento conocido coincide con uno aprobado, está permitido
            if (normalizedApprovedTokens.Any(a =>
                    a == normKnown ||
                    a == normKnown + "s" ||
                    a == normKnown + "es" ||
                    normKnown == a + "s" ||
                    normKnown == a + "es"))
            {
                continue;
            }

            if (ContainsWholePhraseOrPlural(maskedText, normKnown))
            {
                unapprovedFood = knownFood;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Valida por lista blanca los términos que aparecen en posiciones sintácticas de enumeración de alimentos
    /// en la composición del plato, rechazando cualquier alimento desconocido aunque no figure en KnownFoodCatalog.
    /// </summary>
    private static bool TryFindUnapprovedFoodInPlateSlots(
        string plateComposition,
        IReadOnlyList<string> approvedFoods,
        NutritionRecommendationRequestDto request,
        out string unapprovedFood)
    {
        unapprovedFood = string.Empty;
        var normalizedPlate = TextNormalizer.Normalize(plateComposition);
        if (string.IsNullOrEmpty(normalizedPlate))
            return false;

        var sentinelPlate = normalizedPlate;
        var approvedSingleTokens = new HashSet<string>(StringComparer.Ordinal);

        foreach (var approved in approvedFoods
                     .Select(TextNormalizer.Normalize)
                     .Where(a => !string.IsNullOrEmpty(a))
                     .OrderByDescending(a => a.Length))
        {
            sentinelPlate = ReplaceWholePhraseAndPlurals(sentinelPlate, approved, ApprovedFoodSentinel);

            foreach (var token in approved.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                approvedSingleTokens.Add(token);
                approvedSingleTokens.Add(token + "s");
                approvedSingleTokens.Add(token + "es");
            }
        }

        // Permitir palabras que provienen del objetivo o momento enviado por el PF (para no generar falsos positivos)
        var contextTokens = ExtractSimpleWordTokens($"{request.AthleticGoal} {request.Moment}");

        foreach (Match match in FoodSlotCueRegex.Matches(sentinelPlate))
        {
            if (!match.Success || match.Groups.Count < 2)
                continue;

            var candidate = match.Groups[1].Value;
            if (string.IsNullOrEmpty(candidate) ||
                candidate == ApprovedFoodSentinel ||
                char.IsDigit(candidate[0]) ||
                AllowedNonFoodSlotTokens.Contains(candidate) ||
                approvedSingleTokens.Contains(candidate) ||
                contextTokens.Contains(candidate))
            {
                continue;
            }

            unapprovedFood = candidate;
            return true;
        }

        return false;
    }

    private static HashSet<string> ExtractSimpleWordTokens(string? text)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var normalized = TextNormalizer.Normalize(text);
        if (string.IsNullOrEmpty(normalized))
            return tokens;

        foreach (Match match in Regex.Matches(normalized, @"[a-z]+"))
        {
            tokens.Add(match.Value);
        }

        return tokens;
    }

    private static string ReplaceWholePhraseAndPlurals(string normalizedText, string normalizedPhrase, string replacement)
    {
        var variants = new[]
        {
            normalizedPhrase + "es",
            normalizedPhrase + "s",
            normalizedPhrase
        };

        var result = normalizedText;
        foreach (var variant in variants)
        {
            var pattern = $@"(?<![a-z0-9_]){Regex.Escape(variant)}(?![a-z0-9_])";
            result = Regex.Replace(result, pattern, replacement);
        }

        return result;
    }

    private static IReadOnlyList<string> ExtractMatchedApprovedFoods(
        string text,
        IReadOnlyList<string> declaredFoods,
        IReadOnlyList<string> approvedFoods)
    {
        var normalizedText = TextNormalizer.Normalize(text);
        var matched = new List<string>();

        foreach (var approved in approvedFoods)
        {
            var normApproved = TextNormalizer.Normalize(approved);
            if (string.IsNullOrEmpty(normApproved))
                continue;

            var inText = ContainsWholePhraseOrPlural(normalizedText, normApproved);
            var inDeclared = declaredFoods.Any(d => MatchesAnyApprovedFood(d, [approved]));

            if (inText || inDeclared)
            {
                matched.Add(approved);
            }
        }

        return matched;
    }

    private static string MaskWholePhraseAndPlurals(string normalizedText, string normalizedPhrase)
    {
        var variants = new[]
        {
            normalizedPhrase + "es",
            normalizedPhrase + "s",
            normalizedPhrase
        };

        var result = normalizedText;
        foreach (var variant in variants)
        {
            var pattern = $@"(?<![a-z0-9]){Regex.Escape(variant)}(?![a-z0-9])";
            result = Regex.Replace(result, pattern, " ");
        }

        return result;
    }

    private static bool ContainsWholePhraseOrPlural(string normalizedText, string normalizedPhrase)
    {
        if (string.IsNullOrEmpty(normalizedText) || string.IsNullOrEmpty(normalizedPhrase))
            return false;

        var variants = new[]
        {
            normalizedPhrase,
            normalizedPhrase + "s",
            normalizedPhrase + "es"
        };

        foreach (var variant in variants)
        {
            var pattern = $@"(?<![a-z0-9]){Regex.Escape(variant)}(?![a-z0-9])";
            if (Regex.IsMatch(normalizedText, pattern))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryGetStringProperty(JsonElement element, params string[] candidateNames)
    {
        foreach (var name in candidateNames)
        {
            if (TryGetPropertyCaseInsensitive(element, name, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                var val = prop.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(val))
                    return val;
            }
        }

        return null;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value))
            return true;

        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
