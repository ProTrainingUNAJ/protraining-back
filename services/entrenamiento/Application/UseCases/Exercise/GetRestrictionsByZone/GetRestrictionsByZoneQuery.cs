using Domain.Entities;

namespace Application.UseCases.Exercise.GetRestrictionsByZone;

// Consulta de solo lectura para obtener restricciones asociadas a una zona de dolor (uso MCP E51).
public sealed record GetRestrictionsByZoneQuery(string? Zone)
{
    public static IReadOnlyList<string> ResolveRestrictionZones(string? zone)
    {
        if (string.IsNullOrWhiteSpace(zone))
            return [];

        var normalized = zone.Trim().ToLowerInvariant();

        // "rodilla" es una zona de dolor reportada; se asocia a restricciones de músculos de la articulación.
        if (normalized is "rodilla" or "knee")
            return ["cuadriceps", "isquiotibiales", "gemelos"];

        if (normalized is "hombro" or "shoulder")
            return ["hombros"];

        if (normalized is "espalda" or "back")
            return ["espalda_alta", "espalda_media", "espalda_baja"];

        if (normalized is "gluteo")
            return ["gluteos"];

        if (normalized is "gemelo")
            return ["gemelos"];

        return ExerciseRestriction.ValidZones.Contains(normalized)
            ? [normalized]
            : [];
    }
}
