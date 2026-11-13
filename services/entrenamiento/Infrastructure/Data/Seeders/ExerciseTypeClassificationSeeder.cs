using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Infrastructure.Data.Seeders;

public class ExerciseTypeClassificationSeeder
{
    private const string FileName = "exercises.json";

    public static async Task SeedAsync(AppDbContext context, string dataDirectoryPath, ILogger logger)
    {
        var jsonFilePath = Path.Combine(dataDirectoryPath, FileName);
        if (!File.Exists(jsonFilePath))
        {
            logger.LogWarning("No se encontró exercises.json en {Path} para clasificar tipos.", jsonFilePath);
            return;
        }

        // 1. Leemos exercises.json y armamos un diccionario por sourceId (id de free-exercise-db) y por nombre
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonFilePath));

        var metaBySourceId = new Dictionary<string, (string? Category, string? Equipment)>(StringComparer.OrdinalIgnoreCase);
        var metaByName = new Dictionary<string, (string? Category, string? Equipment)>(StringComparer.OrdinalIgnoreCase);

        foreach (var elem in doc.RootElement.EnumerateArray())
        {
            var category = elem.TryGetProperty("category", out var c) ? c.GetString() : null;
            var equipment = elem.TryGetProperty("equipment", out var eq) ? eq.GetString() : null;

            if (elem.TryGetProperty("id", out var idProp) && idProp.GetString() is { Length: > 0 } id)
            {
                metaBySourceId[id.Trim()] = (category, equipment);
            }

            if (elem.TryGetProperty("name", out var nameProp) && nameProp.GetString() is { Length: > 0 } name)
            {
                metaByName[name.Trim()] = (category, equipment);
            }
        }

        // 2. Traemos SOLO ejercicios del catálogo que hoy estén como Weight. los que ya sean Distance, Time o Reps no se tocan.
        var exercisesToClassify = await context.Exercises
            .IgnoreQueryFilters()
            .Where(e => e.PfId == Guid.Empty && e.DeletedAt == null && e.ExerciseType == ExerciseType.Weight)
            .ToListAsync();

        if (exercisesToClassify.Count == 0)
        {
            return;
        }

        var updatedCount = 0;

        foreach (var exercise in exercisesToClassify)
        {
            // Buscamos metadata primero por el sourceId extraído de las fotos (método público ya existente)
            var sourceId = ExerciseNameSyncSeeder.SourceIdFromImages(exercise.Images);
            (string? Category, string? Equipment) meta = default;

            if (sourceId == null || !metaBySourceId.TryGetValue(sourceId, out meta))
            {
                // Fallback por nombre exacto
                if (!metaByName.TryGetValue(exercise.Name, out meta))
                {
                    continue;
                }
            }

            var targetType = DetermineExerciseType(meta.Category, meta.Equipment);

            // Si la regla dice que no es Weight, lo actualizamos
            if (targetType != ExerciseType.Weight)
            {
                exercise.UpdateExerciseType(targetType);
                updatedCount++;
            }
        }

        if (updatedCount > 0)
        {
            await context.SaveChangesAsync();
            logger.LogInformation("KAN-453: Se reclasificaron {Count} ejercicios del catálogo según su categoría.", updatedCount);
        }
    }

    /* plyometrics -> Reps
       stretching -> Time
       cardio -> Time
       strength con equipo "solo el cuerpo" -> Reps
       El resto -> Weight */
    public static ExerciseType DetermineExerciseType(string? category, string? equipment)
    {
        var cat = category?.Trim().ToLowerInvariant();
        var eq = equipment?.Trim().ToLowerInvariant();

        if (cat == "plyometrics")
            return ExerciseType.Reps;

        if (cat is "stretching" or "cardio")
            return ExerciseType.Time;

        if (cat == "strength" && eq == "solo el cuerpo")
            return ExerciseType.Reps;

        return ExerciseType.Weight;
    }
}