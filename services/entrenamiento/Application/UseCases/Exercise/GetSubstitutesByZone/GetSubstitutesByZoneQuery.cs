using Domain.Entities;

namespace Application.UseCases.Exercise.GetSubstitutesByZone;

// Consulta de solo lectura para obtener sustitutos asociados a una zona de dolor (uso MCP E51).
public sealed record GetSubstitutesByZoneQuery(string? Zone)
{
    public static IReadOnlyList<string> ResolveRestrictionZones(string? zone)
    {
        if (string.IsNullOrWhiteSpace(zone))
            return [];

        var normalized = zone.Trim().ToLowerInvariant();

        // "rodilla" es una zona de dolor reportada; se asocia a restricciones de músculos de la articulación.
        if (normalized is "rodilla" or "knee")
            return ["cuadriceps", "isquiotibiales", "gemelos"];

        return ExerciseRestriction.ValidZones.Contains(normalized)
            ? [normalized]
            : [];
    }
}
