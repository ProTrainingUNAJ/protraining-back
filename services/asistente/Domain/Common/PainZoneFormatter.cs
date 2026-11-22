using System.Globalization;

namespace Domain.Common;

/// <summary>
/// Convierte la cadena cruda de zonas de dolor (ej. "biceps_izquierdo:6,pecho_derecho:5")
/// en etiquetas legibles en español para avisos preventivos.
/// </summary>
public static class PainZoneFormatter
{
    private static readonly IReadOnlyDictionary<string, string> ZoneLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Músculos oficiales del mapa anatómico
            ["pecho_derecho"] = "Pecho derecho",
            ["pecho_izquierdo"] = "Pecho izquierdo",
            ["hombro_derecho"] = "Hombro derecho",
            ["hombro_izquierdo"] = "Hombro izquierdo",
            ["cuello_trapecio_derecho"] = "Cuello y trapecio derecho",
            ["cuello_trapecio_izquierdo"] = "Cuello y trapecio izquierdo",
            ["biceps_derecho"] = "Bíceps derecho",
            ["biceps_izquierdo"] = "Bíceps izquierdo",
            ["triceps_derecho"] = "Tríceps derecho",
            ["triceps_izquierdo"] = "Tríceps izquierdo",
            ["abdominales"] = "Abdominales",
            ["cuadriceps_derecho"] = "Cuádriceps derecho",
            ["cuadriceps_izquierdo"] = "Cuádriceps izquierdo",
            ["gluteo_derecho"] = "Glúteo derecho",
            ["gluteo_izquierdo"] = "Glúteo izquierdo",
            ["isquiotibial_derecho"] = "Isquiotibial derecho",
            ["isquiotibial_izquierdo"] = "Isquiotibial izquierdo",
            ["gemelo_derecho"] = "Gemelo derecho",
            ["gemelo_izquierdo"] = "Gemelo izquierdo",
            ["espalda_alta"] = "Espalda alta",
            ["espalda_media"] = "Espalda media (Dorsales)",
            ["espalda_baja"] = "Espalda baja (Lumbares)",
            ["antebrazo_derecho"] = "Antebrazo derecho",
            ["antebrazo_izquierdo"] = "Antebrazo izquierdo",
            ["mano_derecha"] = "Mano derecha",
            ["mano_izquierda"] = "Mano izquierda",
            ["pie_derecho"] = "Pie derecho",
            ["pie_izquierdo"] = "Pie izquierdo",

            // Articulaciones oficiales del mapa anatómico
            ["rodilla_derecha"] = "Rodilla derecha",
            ["rodilla_izquierda"] = "Rodilla izquierda",
            ["codo_derecho"] = "Codo derecho",
            ["codo_izquierdo"] = "Codo izquierdo",
            ["muneca_derecha"] = "Muñeca derecha",
            ["muneca_izquierda"] = "Muñeca izquierda",
            ["tobillo_derecho"] = "Tobillo derecho",
            ["tobillo_izquierdo"] = "Tobillo izquierdo",

            // Zonas de ficha clínica / legado sin lateralidad
            ["pecho"] = "Pecho",
            ["hombros"] = "Hombros",
            ["hombro"] = "Hombro",
            ["biceps"] = "Bíceps",
            ["triceps"] = "Tríceps",
            ["cuadriceps"] = "Cuádriceps",
            ["gluteos"] = "Glúteos",
            ["isquiotibiales"] = "Isquiotibiales",
            ["gemelos"] = "Gemelos",
            ["trapecios"] = "Trapecios",
            ["dorsales"] = "Dorsales",
            ["oblicuos"] = "Oblicuos",
            ["antebrazos"] = "Antebrazos",
            ["manos"] = "Manos",
            ["pies"] = "Pies",
            ["cuello_trapecios"] = "Cuello y trapecios",
            ["lumbar"] = "Lumbar",
            ["rodilla"] = "Rodilla"
        };

    public static string Format(string? rawPainZones)
    {
        if (string.IsNullOrWhiteSpace(rawPainZones))
        {
            return string.Empty;
        }

        var formattedEntries = new List<string>();

        foreach (var rawEntry in rawPainZones.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawEntry is "[]" or "{}")
            {
                continue;
            }

            var separatorIndex = rawEntry.LastIndexOf(':');
            if (separatorIndex <= 0)
            {
                // Mantiene compatibilidad con descripciones en texto libre sin ":nivel"
                formattedEntries.Add(rawEntry);
                continue;
            }

            var zoneId = rawEntry[..separatorIndex].Trim();
            var levelRaw = rawEntry[(separatorIndex + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(zoneId))
            {
                continue;
            }

            var zoneLabel = ResolveZoneLabel(zoneId);
            var levelLabel = FormatLevel(levelRaw);

            formattedEntries.Add(levelLabel is not null
                ? $"{zoneLabel} [{levelLabel}]"
                : zoneLabel);
        }

        return string.Join(", ", formattedEntries);
    }

    private static string ResolveZoneLabel(string zoneId)
    {
        if (ZoneLabels.TryGetValue(zoneId, out var label))
        {
            return label;
        }

        var normalized = zoneId.Replace('_', ' ').Trim();
        if (normalized.Length == 0)
        {
            return zoneId;
        }

        return char.ToUpper(normalized[0], new CultureInfo("es-AR")) + normalized[1..];
    }

    private static string? FormatLevel(string levelRaw)
    {
        if (!int.TryParse(levelRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) || level <= 0)
        {
            return null;
        }

        return level >= 6
            ? "Nivel 6 - Lesión activa"
            : $"Nivel {level}";
    }
}