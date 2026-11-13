using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Infrastructure.Data.Seeders;

public class ExerciseRestrictionSeeder
{
    public static async Task SeedAsync(AppDbContext context, string dataDirectoryPath, ILogger logger)
    {
        if (await context.ExerciseRestrictions.IgnoreQueryFilters().AnyAsync())
        {
            return;
        }

        var jsonFilePath = Path.Combine(dataDirectoryPath, "exercise-restrictions.json");

        if (!File.Exists(jsonFilePath))
        {
            logger.LogWarning("No se encontró el archivo de restricciones en {Path}", jsonFilePath);
            return;
        }

        var jsonContent = await File.ReadAllTextAsync(jsonFilePath);
        using var doc = JsonDocument.Parse(jsonContent);

        var allExercises = await context.Exercises
            .IgnoreQueryFilters()
            .Select(e => new { e.Id, e.Name })
            .ToListAsync();

        var exerciseMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var ex in allExercises)
        {
            exerciseMap.TryAdd(ex.Name.Trim(), ex.Id);
        }

        var elements = ParseRestrictions(doc.RootElement);
        var restrictionsToAdd = new List<ExerciseRestriction>();

        foreach (var item in elements)
        {
            var normalizedZone = item.Zone.Trim().ToLowerInvariant();

            if (!ExerciseRestriction.ValidZones.Contains(normalizedZone))
            {
                logger.LogWarning("La zona '{Zone}' no es una zona de dolor válida para la restricción de '{Exercise}'. Se omite.", item.Zone, item.ExerciseName);
                continue;
            }

            if (!exerciseMap.TryGetValue(item.ExerciseName.Trim(), out var exerciseId))
            {
                logger.LogWarning("No se encontró el ejercicio '{ExerciseName}' en el catálogo para la restricción en la zona '{Zone}'. Se omite.", item.ExerciseName, item.Zone);
                continue;
            }

            if (restrictionsToAdd.Any(r => r.Zone.Equals(normalizedZone, StringComparison.OrdinalIgnoreCase) && r.ExerciseId == exerciseId))
            {
                continue;
            }

            var restriction = ExerciseRestriction.Create(
                zone: normalizedZone,
                exerciseId: exerciseId,
                reason: string.IsNullOrWhiteSpace(item.Reason) ? "Restricción biomecánica por dolor en zona." : item.Reason,
                minPainLevel: item.MinPainLevel <= 0 ? 1 : item.MinPainLevel
            );

            foreach (var subName in item.Substitutes)
            {
                if (!exerciseMap.TryGetValue(subName.Trim(), out var substituteId))
                {
                    logger.LogWarning("No se encontró el ejercicio sustituto '{SubstituteName}' en el catálogo para la restricción de '{ExerciseName}'. Se omite.", subName, item.ExerciseName);
                    continue;
                }

                if (substituteId == exerciseId)
                {
                    logger.LogWarning("El ejercicio sustituto '{SubstituteName}' no puede ser igual al ejercicio restringido. Se omite.", subName);
                    continue;
                }

                if (restriction.Substitutes.Any(s => s.SubstituteExerciseId == substituteId))
                {
                    continue;
                }

                restriction.AddSubstitute(substituteId, restriction.Substitutes.Count + 1);
            }

            restrictionsToAdd.Add(restriction);
        }

        if (restrictionsToAdd.Count > 0)
        {
            await context.ExerciseRestrictions.AddRangeAsync(restrictionsToAdd);
            await context.SaveChangesAsync();
            logger.LogInformation("Se importaron exitosamente {Count} restricciones biomecánicas al catálogo.", restrictionsToAdd.Count);
        }
    }

    private static List<(string Zone, string ExerciseName, int MinPainLevel, string Reason, List<string> Substitutes)> ParseRestrictions(JsonElement root)
    {
        var list = new List<(string Zone, string ExerciseName, int MinPainLevel, string Reason, List<string> Substitutes)>();

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var elem in root.EnumerateArray())
            {
                var zone = elem.TryGetProperty("zone", out var z) ? z.GetString() : null;
                var exName = elem.TryGetProperty("exercise", out var ex) ? ex.GetString()
                    : elem.TryGetProperty("exerciseName", out var exn) ? exn.GetString()
                    : elem.TryGetProperty("name", out var n) ? n.GetString() : null;
                var minPain = elem.TryGetProperty("minPainLevel", out var mp) ? mp.GetInt32()
                    : elem.TryGetProperty("min_pain_level", out var mp2) ? mp2.GetInt32() : 1;
                var reason = elem.TryGetProperty("reason", out var r) ? r.GetString()
                    : elem.TryGetProperty("motivo", out var mot) ? mot.GetString() : "Restricción biomecánica.";

                var substitutes = ReadSubstitutes(elem);

                if (!string.IsNullOrWhiteSpace(zone) && !string.IsNullOrWhiteSpace(exName))
                {
                    list.Add((zone, exName, minPain, reason ?? "Restricción biomecánica.", substitutes));
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                var zone = prop.Name;
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in prop.Value.EnumerateArray())
                    {
                        var exName = elem.TryGetProperty("exercise", out var ex) ? ex.GetString()
                            : elem.TryGetProperty("exerciseName", out var exn) ? exn.GetString()
                            : elem.TryGetProperty("name", out var n) ? n.GetString() : null;
                        var minPain = elem.TryGetProperty("minPainLevel", out var mp) ? mp.GetInt32()
                            : elem.TryGetProperty("min_pain_level", out var mp2) ? mp2.GetInt32() : 1;
                        var reason = elem.TryGetProperty("reason", out var r) ? r.GetString()
                            : elem.TryGetProperty("motivo", out var mot) ? mot.GetString() : "Restricción biomecánica.";

                        var substitutes = ReadSubstitutes(elem);

                        if (!string.IsNullOrWhiteSpace(exName))
                        {
                            list.Add((zone, exName, minPain, reason ?? "Restricción biomecánica.", substitutes));
                        }
                    }
                }
            }
        }

        return list;
    }

    private static List<string> ReadSubstitutes(JsonElement elem)
    {
        var list = new List<string>();
        var subsProp = elem.TryGetProperty("substitutes", out var s) ? s
            : elem.TryGetProperty("sustitutos", out var s2) ? s2 : default;

        if (subsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var sub in subsProp.EnumerateArray())
            {
                if (sub.ValueKind == JsonValueKind.String)
                {
                    var val = sub.GetString();
                    if (!string.IsNullOrWhiteSpace(val)) list.Add(val);
                }
                else if (sub.ValueKind == JsonValueKind.Object)
                {
                    var subName = sub.TryGetProperty("exercise", out var sex) ? sex.GetString()
                        : sub.TryGetProperty("exerciseName", out var sexn) ? sexn.GetString()
                        : sub.TryGetProperty("name", out var sn) ? sn.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(subName)) list.Add(subName);
                }
            }
        }

        return list;
    }
}
