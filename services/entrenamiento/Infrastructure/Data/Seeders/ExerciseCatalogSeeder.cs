using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Infrastructure.Data.Seeders
{
    public class ExerciseCatalogSeeder
    {
        private const string FreeExerciseDbRawUrl = "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/";
        public static async Task SeedAsync(AppDbContext context, string dataDirectoryPath, ILogger logger)
        {

            if (await context.Exercises.IgnoreQueryFilters().AnyAsync())
            {
                return;
            }

            var jsonFilePath = Path.Combine(dataDirectoryPath, "exercises.json");

            if (File.Exists(jsonFilePath))
            {
                var jsonContent = await File.ReadAllTextAsync(jsonFilePath);

                using var doc = JsonDocument.Parse(jsonContent);

                var exercises = new List<Exercise>();

                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    var name = elem.GetProperty("name").GetString()!;

                    var description = elem.TryGetProperty("instructions", out var inst)
                        ? string.Join(" ", inst.EnumerateArray().Select(s => s.GetString()))
                        : null;

                    var equipment = elem.TryGetProperty("equipment", out var eq) ? eq.GetString() : null;

                    var images = ReadStringArray(elem, "images", FreeExerciseDbRawUrl);

                    var primaryMuscles = ReadStringArray(elem, "primaryMuscles");

                    var secondaryMuscles = ReadStringArray(elem, "secondaryMuscles");

                    // Mapear grupo muscular con el primer primaryMuscle
                    var firstMuscle = primaryMuscles.FirstOrDefault() ?? "general";

                    exercises.Add(Exercise.Create(
                        name: name,
                        muscleGroup: NormalizeMuscleGroup(firstMuscle),
                        description: description,
                        equipment: equipment,
                        images: images,
                        primaryMuscles: primaryMuscles,
                        secondaryMuscles: secondaryMuscles
                    ));
                }

                await context.Exercises.AddRangeAsync(exercises);
                await context.SaveChangesAsync();
                logger.LogInformation("Se importaron exitosamente {Count} ejercicios al catálogo.", exercises.Count);
            }
            else
            {
                logger.LogWarning("No se encontró el archivo de ejercicios en {Path}", jsonFilePath);
            }
        }

        private static readonly Dictionary<string, string> MuscleGroupMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // Pecho (84)
            ["chest"] = "Pecho",
            ["pecho"] = "Pecho",

            // Espalda (114)
            ["lats"] = "Espalda",
            ["middle back"] = "Espalda",
            ["espalda media"] = "Espalda",
            ["lower back"] = "Espalda",
            ["espalda baja"] = "Espalda",
            ["traps"] = "Espalda",
            ["trapecios"] = "Espalda",

            // Piernas (298)
            ["quadriceps"] = "Piernas",
            ["cuadríceps"] = "Piernas",
            ["hamstrings"] = "Piernas",
            ["isquiotibiales"] = "Piernas",
            ["calves"] = "Piernas",
            ["gemelos"] = "Piernas",
            ["glutes"] = "Piernas",
            ["glúteos"] = "Piernas",
            ["adductors"] = "Piernas",
            ["aductores"] = "Piernas",
            ["abductors"] = "Piernas",
            ["abductores"] = "Piernas",

            // Hombros (137)
            ["shoulders"] = "Hombros",
            ["hombros"] = "Hombros",
            ["neck"] = "Hombros",
            ["cuello"] = "Hombros",

            // Brazos (151)
            ["biceps"] = "Brazos",
            ["bíceps"] = "Brazos",
            ["triceps"] = "Brazos",
            ["tríceps"] = "Brazos",
            ["forearms"] = "Brazos",
            ["antebrazos"] = "Brazos",

            // Core (93)
            ["abdominals"] = "Core",
            ["abdominales"] = "Core"
        };

        public static string NormalizeMuscleGroup(string muscle) =>
            MuscleGroupMap.TryGetValue(muscle.Trim(), out var group) ? group : "Cuerpo Completo";

        private static List<string> ReadStringArray(JsonElement elem, string propertyName, string? urlPrefix = null)
        {
            var list = new List<string>();

            if (elem.TryGetProperty(propertyName, out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var val = item.GetString();
                    if (!string.IsNullOrWhiteSpace(val))
                        list.Add(urlPrefix != null ? $"{urlPrefix}{val}" : val);
                }
            }

            return list;
        }

    }
}
