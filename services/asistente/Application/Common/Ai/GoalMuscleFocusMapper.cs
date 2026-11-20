using Application.Common.Helpers;
using Application.DTOs.Ai;

namespace Application.Common.Ai;

/// <summary>
/// Mapeo compartido entre el texto del objetivo escrito por el PF y los grupos musculares foco (KAN-563 / KAN-605).
/// Lo usan tanto <see cref="RoutinePromptBuilder"/> (prompt de IA) como el motor de reglas determinísticas
/// para mantenerse siempre sincronizados.
/// </summary>
public static class GoalMuscleFocusMapper
{
    private static readonly IReadOnlyDictionary<string, string[]> GroupAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Piernas"] = ["pierna", "cuadriceps", "isquio", "gluteo", "gemelo", "pantorrilla", "tren inferior", "lower body", "legs"],
            ["Pecho"] = ["pecho", "pectoral", "empuje", "push", "chest"],
            ["Espalda"] = ["espalda", "dorsal", "trapecio", "lumbar", "traccion", "pull"],
            ["Hombros"] = ["hombro", "deltoid", "shoulder"],
            ["Brazos"] = ["brazo", "biceps", "triceps", "antebrazo", "arms"],
            ["Core"] = ["core", "abdom", "zona media"]
        };

    /// <summary>
    /// Extrae los grupos musculares clave del texto del objetivo (sin tildes ni mayúsculas, español e inglés).
    /// Devuelve lista vacía si el objetivo no pide un foco muscular específico.
    /// </summary>
    public static IReadOnlyList<string> ExtractMuscleGroups(string? goal)
    {
        if (string.IsNullOrWhiteSpace(goal))
            return [];

        var text = TextNormalizer.Normalize(goal);
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Piernas / Tren Inferior
        if (text.Contains("pierna") || text.Contains("cuadriceps") ||
            text.Contains("isquio") || text.Contains("gluteo") ||
            text.Contains("gemelo") || text.Contains("pantorrilla") ||
            text.Contains("tren inferior") || text.Contains("lower body") ||
            text.Contains("sentadilla") || text.Contains("peso muerto") ||
            text.Contains("prensa") || text.Contains("estocada") || text.Contains("zancada") ||
            text.Contains("hip thrust") || text.Contains("empuje de cadera") ||
            text.Contains("legs") || text.Contains("squat") ||
            text.Contains("deadlift") || text.Contains("lunge"))
        {
            groups.Add("Piernas");
        }

        // Pecho / Empuje
        if (text.Contains("pecho") || text.Contains("pectoral") ||
            text.Contains("empuje") || text.Contains("push") ||
            text.Contains("press de banca") || text.Contains("press banca") ||
            text.Contains("fondo") || text.Contains("chest") || text.Contains("bench press"))
        {
            groups.Add("Pecho");
        }

        // Espalda / Tracción
        if (text.Contains("espalda") || text.Contains("dorsal") ||
            text.Contains("traccion") || text.Contains("pull") ||
            text.Contains("remo") || text.Contains("dominada") ||
            text.Contains("lumbar") || text.Contains("trapecio") ||
            text.Contains("jalon") || text.Contains("upper back") || text.Contains("lower back"))
        {
            groups.Add("Espalda");
        }

        // Hombros
        if (text.Contains("hombro") || text.Contains("deltoides") ||
            text.Contains("press militar") || text.Contains("shoulder"))
        {
            groups.Add("Hombros");
        }

        // Brazos
        if (text.Contains("brazo") || text.Contains("biceps") ||
            text.Contains("triceps") || text.Contains("arms"))
        {
            groups.Add("Brazos");
        }

        // Tren superior: mapea a Pecho, Espalda, Hombros y Brazos para evitar que entren piernas
        if (text.Contains("tren superior") || text.Contains("upper body") || text.Contains("torso"))
        {
            groups.Add("Pecho");
            groups.Add("Espalda");
            groups.Add("Hombros");
            groups.Add("Brazos");
        }

        // Core / Abdomen
        if (text.Contains("core") || text.Contains("abdominal") ||
            text.Contains("abdomen") || text.Contains("zona media") || text.Contains("plancha"))
        {
            groups.Add("Core");
        }

        // Deportes de contacto / Rugby (potencia de tren inferior, tracción y estabilidad de core)
        if (text.Contains("rugby") || text.Contains("contacto") || text.Contains("colision"))
        {
            groups.Add("Piernas");
            groups.Add("Espalda");
            groups.Add("Core");
        }

        return groups.ToList();
    }

    /// <summary>
    /// Determina si un ejercicio del catálogo pertenece a alguno de los grupos musculares objetivo.
    /// Prioriza <see cref="CatalogExerciseDto.MuscleGroup"/> cuando está informado para evitar falsos positivos por nombre.
    /// </summary>
    public static bool MatchesMuscleGroups(
        CatalogExerciseDto exercise,
        IReadOnlyCollection<string> targetGroups)
    {
        if (exercise is null || targetGroups is null || targetGroups.Count == 0)
            return false;

        var normGroup = TextNormalizer.Normalize(exercise.MuscleGroup);
        var normName = TextNormalizer.Normalize(exercise.Name);

        foreach (var target in targetGroups)
        {
            var normTarget = TextNormalizer.Normalize(target);
            if (string.IsNullOrEmpty(normTarget))
                continue;

            var aliases = GroupAliases.TryGetValue(target, out var mapped)
                ? mapped
                : [normTarget];

            if (!string.IsNullOrEmpty(normGroup))
            {
                if (normGroup.Contains(normTarget) ||
                    normTarget.Contains(normGroup) ||
                    aliases.Any(alias => normGroup.Contains(alias)))
                {
                    return true;
                }
            }
            else if (!string.IsNullOrEmpty(normName))
            {
                if (normName.Contains(normTarget) ||
                    aliases.Any(alias => normName.Contains(alias)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
