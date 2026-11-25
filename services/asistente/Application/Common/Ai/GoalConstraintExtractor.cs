using System.Text.RegularExpressions;
using Application.Common.Helpers;

namespace Application.Common.Ai;

/// <summary>
/// Extrae restricciones y pedidos explícitos del preparador físico escritos en el texto libre del objetivo
/// (por ejemplo: cantidad de ejercicios por día/sesión o zonas articulares a cuidar/priorizar · KAN-652).
/// </summary>
public static class GoalConstraintExtractor
{
    public const int MinAllowedExercisesPerSession = 1;
    public const int MaxAllowedExercisesPerSession = 12;

    private static readonly IReadOnlyDictionary<string, int> WordNumbers =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["un"] = 1,
            ["uno"] = 1,
            ["una"] = 1,
            ["dos"] = 2,
            ["tres"] = 3,
            ["cuatro"] = 4,
            ["cinco"] = 5,
            ["seis"] = 6,
            ["siete"] = 7,
            ["ocho"] = 8,
            ["nueve"] = 9,
            ["diez"] = 10,
            ["once"] = 11,
            ["doce"] = 12,
            ["trece"] = 13,
            ["catorce"] = 14,
            ["quince"] = 15,
            ["dieciseis"] = 16,
            ["diecisiete"] = 17,
            ["dieciocho"] = 18,
            ["diecinueve"] = 19,
            ["veinte"] = 20,
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3,
            ["four"] = 4,
            ["five"] = 5,
            ["six"] = 6,
            ["seven"] = 7,
            ["eight"] = 8,
            ["nine"] = 9,
            ["ten"] = 10,
            ["eleven"] = 11,
            ["twelve"] = 12,
            ["thirteen"] = 13,
            ["fourteen"] = 14,
            ["fifteen"] = 15,
            ["twenty"] = 20
        };

    private const string NumberTokenPattern =
        @"(?<num>\d{1,3}|un|uno|una|dos|tres|cuatro|cinco|seis|siete|ocho|nueve|diez|once|doce|trece|catorce|quince|dieciseis|diecisiete|dieciocho|diecinueve|veinte|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|twenty)";

    // Tolera variantes y errores de tipeo frecuentes en "ejercicios" (ej. "ejericcios", "ejercicos", "ejersicios", "ej", "exercises")
    private const string ExerciseWordPattern =
        @"(?:ejer[a-z]*|ej[a-z]*cios?|ejs?\.?|exercises?)";

    // Si después de "ejercicio(s)" viene "de <algo>" ("de core", "de movilidad") o un modificador de tipo/ubicación
    // ("pliométrico", "al inicio", "al final"), se refiere a un ejercicio puntual y NO al total de la sesión (KAN-652).
    private const string NotSpecificExercisePartLookahead =
        @"(?!\s+(?:de\s+(?!entrenamiento\b)[a-z]+|pliometric\w*|isometric\w*|unilateral\w*|bilateral\w*|accesori\w*|analitic\w*|preventiv\w*|compensatori\w*|aislad\w*|extra\w*|adicional\w*|al\s+inicio|al\s+final|al\s+principio|para\s+empezar|para\s+terminar|para\s+cerrar))";

    // 1. "rutina de N ejercicios", "rutina de N" (sin "días/semanas/series/..."), "sesión/sesiones de N [ejercicios]"
    private static readonly Regex RoutineOrSessionCountRegex = new(
        $@"\b(?:rutinas?|sesiones?|sesion)\s+(?:con\s+|de\s+)(?:(?:como\s+max\w*|max\w*|hasta|exactamente|solo|solamente|unicamente)\s+)*{NumberTokenPattern}(?:\s+{ExerciseWordPattern}{NotSpecificExercisePartLookahead}|\b(?!\s+(?:dias?|semanas?|series?|repeticiones?|reps?|bloques?|minutos?|min\b|meses?|horas?|kg\b|kilos?)))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 2. "<num> ejercicios por día / por sesión / en total / diarios / cada día / cada sesión / como máximo / máximo / tope"
    private static readonly Regex PerSessionOrMaxSuffixRegex = new(
        $@"\b{NumberTokenPattern}\s+{ExerciseWordPattern}{NotSpecificExercisePartLookahead}\s+(?:por\s+dia|por\s+sesion|diarios?|cada\s+dia|cada\s+sesion|en\s+total|como\s+max\w*|max\w*|como\s+tope|tope|per\s+day|per\s+session|in\s+total)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 3. "máximo / como máximo / tope / hasta / no más de / exactamente / solo <num> ejercicios"
    private static readonly Regex LimitPrefixBeforeExercisesRegex = new(
        $@"\b(?:como\s+max\w*|max\w*|tope(?:\s+de)?|hasta|no\s+mas\s+de|exactamente|exactly|at\s+most|max|solo|solamente|unicamente)\s+(?:de\s+)?{NumberTokenPattern}\s+{ExerciseWordPattern}{NotSpecificExercisePartLookahead}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 4. "por día / por sesión [quiero/tenga/hacer/...] <num> ejercicios" o "(que) la rutina/sesión tenga <num> ejercicios"
    private static readonly Regex SessionContextPrefixRegex = new(
        $@"\b(?:por\s+dia|por\s+sesion|cada\s+dia|cada\s+sesion|(?:que\s+)?(?:la\s+|cada\s+)?(?:rutina|sesion|planificacion)\s+(?:tenga|con|de))\s*(?:(?:quiero|tenga|hacer|incluir|poner|con|de|como\s+max\w*|max\w*|hasta|exactamente|solo)\s+)*{NumberTokenPattern}\s+{ExerciseWordPattern}{NotSpecificExercisePartLookahead}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 5. "ejercicios por día/sesión: <num>" o "máximo/cantidad/tope de ejercicios: <num>"
    private static readonly Regex NumberAfterExercisesContextRegex = new(
        $@"\b(?:(?:max\w*|tope|cantidad|limite|total)\s+de\s+{ExerciseWordPattern}|{ExerciseWordPattern}\s+(?:por\s+dia|por\s+sesion|en\s+total|como\s+max\w*|max\w*))\s*[:=]?\s*{NumberTokenPattern}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly (string CanonicalZone, Regex Pattern)[] ZonePatterns =
    [
        ("rodilla", new Regex(@"\b(?:rodillas?|rotulianos?|meniscos?|cruzados?|lca)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("lumbar", new Regex(@"\b(?:lumbar(?:es)?|espalda\s+baja|ciaticos?|hernias?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("hombro", new Regex(@"\b(?:hombros?|manguito\s+rotador|acromios?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("isquiotibiales", new Regex(@"\b(?:isquiotibial(?:es)?|isquios?|femoral(?:es)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("tobillo", new Regex(@"\b(?:tobillos?|aquiles|esguinces?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("cadera", new Regex(@"\b(?:caderas?|psoiliacos?|pubalgias?|inguinal(?:es)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("aductores", new Regex(@"\b(?:aductor(?:es)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("cervical", new Regex(@"\b(?:cervical(?:es)?|cuello)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("codo", new Regex(@"\b(?:codos?|epicondilitis)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("muñeca", new Regex(@"\b(?:munecas?|carpianos?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled))
    ];

    /// <summary>
    /// Extrae el número crudo de ejercicios por sesión pedido por el PF en el objetivo (sin aplicar el tope de 12).
    /// Retorna <c>null</c> si el objetivo no contiene ninguna directiva referida al total de la sesión o rutina.
    /// </summary>
    public static int? ExtractRawRequestedExercisesPerSession(string? goal)
    {
        if (string.IsNullOrWhiteSpace(goal))
            return null;

        var normalized = TextNormalizer.Normalize(goal);
        if (string.IsNullOrEmpty(normalized))
            return null;

        var orderedPatterns = new[]
        {
            RoutineOrSessionCountRegex,
            PerSessionOrMaxSuffixRegex,
            LimitPrefixBeforeExercisesRegex,
            SessionContextPrefixRegex,
            NumberAfterExercisesContextRegex
        };

        foreach (var regex in orderedPatterns)
        {
            var match = regex.Match(normalized);
            if (!match.Success)
                continue;

            var parsed = TryParseNumberToken(match.Groups["num"].Value);
            if (parsed is >= MinAllowedExercisesPerSession)
                return parsed;
        }

        return null;
    }

    /// <summary>
    /// Extrae la cantidad objetivo de ejercicios por día/sesión pedida por el PF en el objetivo,
    /// acotada al rango permitido [<see cref="MinAllowedExercisesPerSession"/>..<see cref="MaxAllowedExercisesPerSession"/>].
    /// Si el PF pidió más de 12, devuelve 12 (y <see cref="ExtractRawRequestedExercisesPerSession"/> devuelve el valor original para avisar en la nota).
    /// </summary>
    public static int? ExtractMaxExercisesPerSession(string? goal)
    {
        var raw = ExtractRawRequestedExercisesPerSession(goal);
        if (!raw.HasValue)
            return null;

        return Math.Clamp(raw.Value, MinAllowedExercisesPerSession, MaxAllowedExercisesPerSession);
    }

    /// <summary>
    /// Extrae las zonas articulares o anatómicas específicas mencionadas en el objetivo usando límites de palabra completa (\b)
    /// para evitar falsos positivos como "calcar" -> "lca" (KAN-652).
    /// </summary>
    public static IReadOnlyList<string> ExtractProtectedZones(string? goal)
    {
        if (string.IsNullOrWhiteSpace(goal))
            return [];

        var normalized = TextNormalizer.Normalize(goal);
        if (string.IsNullOrEmpty(normalized))
            return [];

        var zones = new List<string>();
        foreach (var (canonicalZone, pattern) in ZonePatterns)
        {
            if (pattern.IsMatch(normalized))
            {
                zones.Add(canonicalZone);
            }
        }

        return zones;
    }

    private static int? TryParseNumberToken(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return null;

        var token = rawToken.Trim();
        if (int.TryParse(token, out var numericValue))
            return numericValue;

        if (WordNumbers.TryGetValue(token, out var wordValue))
            return wordValue;

        return null;
    }
}
