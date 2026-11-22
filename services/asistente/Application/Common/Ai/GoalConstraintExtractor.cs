using System.Text.RegularExpressions;
using Application.Common.Helpers;

namespace Application.Common.Ai;

/// <summary>
/// Extrae restricciones y pedidos explícitos del preparador físico escritos en el texto libre del objetivo
/// (por ejemplo: límite o cantidad de ejercicios por día/sesión, o zonas articulares a cuidar/priorizar).
/// </summary>
public static class GoalConstraintExtractor
{
    public const int MinAllowedExercisesPerSession = 1;
    public const int MaxAllowedExercisesPerSession = 10;

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
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3,
            ["four"] = 4,
            ["five"] = 5,
            ["six"] = 6,
            ["seven"] = 7,
            ["eight"] = 8,
            ["nine"] = 9,
            ["ten"] = 10
        };

    private const string NumberTokenPattern =
        @"(?<num>\d{1,2}|un|uno|una|dos|tres|cuatro|cinco|seis|siete|ocho|nueve|diez|one|two|three|four|five|six|seven|eight|nine|ten)";

    // Tolera variantes y errores de tipeo frecuentes en "ejercicios" (ej. "ejericcios", "ejercicos", "ejersicios", "ej", "exercises")
    private const string ExerciseWordPattern =
        @"(?:ejer[a-z]*|ej[a-z]*cios?|ejs?\.?|exercises?)";

    // Prioridad 1a: "<num> ejercicios como maximo / por dia / por sesion / diarios / en total"
    private static readonly Regex ExplicitSuffixMaxExercisesRegex = new(
        $@"\b{NumberTokenPattern}\s+{ExerciseWordPattern}\s+(?:como\s+max\w*|max\w*|como\s+tope|tope|por\s+dia|por\s+sesion|diarios?|cada\s+dia|cada\s+sesion|en\s+total)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Prioridad 1b: "maximo / tope / hasta / no mas de / por dia quiero / que tenga <num> ejercicios"
    private static readonly Regex ExplicitPrefixMaxExercisesRegex = new(
        $@"\b(?:como\s+max\w*|max\w*|tope|hasta|no\s+mas\s+de|solo|solamente|unicamente|por\s+dia|por\s+sesion|quiero|tenga|hacer|meter|poner|incluir)\s+(?:(?:de|quiero|tenga|hacer|como\s+max\w*|max\w*|hasta|solo)\s+)*{NumberTokenPattern}\s+{ExerciseWordPattern}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Prioridad 2: Cualquier "<num> ejercicios" en el objetivo
    private static readonly Regex GeneralNumberBeforeExercisesRegex = new(
        $@"\b{NumberTokenPattern}\s+{ExerciseWordPattern}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Prioridad 3: "ejercicios: <num>" o "maximo de ejercicios <num>"
    private static readonly Regex NumberAfterExercisesRegex = new(
        $@"\b{ExerciseWordPattern}\s*(?:[:=]|como\s+max\w*|max\w*|tope|por\s+dia|por\s+sesion)?\s*{NumberTokenPattern}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly (string CanonicalZone, string[] Keywords)[] ZonePatterns =
    [
        ("rodilla", ["rodilla", "rotuliano", "menisco", "cruzado", "lca"]),
        ("lumbar", ["lumbar", "espalda baja", "ciatico", "hernia"]),
        ("hombro", ["hombro", "manguito rotador", "acromio"]),
        ("isquiotibiales", ["isquiotibial", "isquio", "femoral"]),
        ("tobillo", ["tobillo", "aquiles", "esguince"]),
        ("cadera", ["cadera", "psoiliaco", "pubalgia", "inguinal"]),
        ("aductores", ["aductor"]),
        ("cervical", ["cervical", "cuello"]),
        ("codo", ["codo", "epicondilitis"]),
        ("muñeca", ["muneca", "carpiano"])
    ];

    /// <summary>
    /// Extrae la cantidad máxima o exacta de ejercicios por día/sesión pedida por el PF en el objetivo.
    /// Retorna <c>null</c> si el objetivo no contiene ninguna directiva sobre cantidad de ejercicios.
    /// </summary>
    public static int? ExtractMaxExercisesPerSession(string? goal)
    {
        if (string.IsNullOrWhiteSpace(goal))
            return null;

        var normalized = TextNormalizer.Normalize(goal);
        if (string.IsNullOrEmpty(normalized))
            return null;

        var orderedPatterns = new[]
        {
            ExplicitSuffixMaxExercisesRegex,
            ExplicitPrefixMaxExercisesRegex,
            GeneralNumberBeforeExercisesRegex,
            NumberAfterExercisesRegex
        };

        foreach (var regex in orderedPatterns)
        {
            var match = regex.Match(normalized);
            if (!match.Success)
                continue;

            var parsed = TryParseNumberToken(match.Groups["num"].Value);
            if (parsed is >= MinAllowedExercisesPerSession and <= MaxAllowedExercisesPerSession)
                return parsed;
        }

        return null;
    }

    /// <summary>
    /// Extrae las zonas articulares o anatómicas específicas mencionadas en el objetivo
    /// (ej. "para rodilla", "cuidando lumbar", "proteger hombro") para reforzar la directiva biomecánica.
    /// </summary>
    public static IReadOnlyList<string> ExtractProtectedZones(string? goal)
    {
        if (string.IsNullOrWhiteSpace(goal))
            return [];

        var normalized = TextNormalizer.Normalize(goal);
        if (string.IsNullOrEmpty(normalized))
            return [];

        var zones = new List<string>();
        foreach (var (canonicalZone, keywords) in ZonePatterns)
        {
            if (keywords.Any(k => normalized.Contains(k, StringComparison.Ordinal)))
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
