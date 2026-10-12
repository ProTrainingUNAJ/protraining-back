using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;

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

                    var primaryMuscles = elem.TryGetProperty("primaryMuscles", out var m) && m.GetArrayLength() > 0
                        ? m[0].GetString()!
                        : "general";

                    var description = elem.TryGetProperty("instructions", out var inst)
                        ? string.Join(" ", inst.EnumerateArray().Select(s => s.GetString()))
                        : null;

                    var imageUrl = elem.TryGetProperty("images", out var imgs) && imgs.GetArrayLength() > 0
                        ? $"{FreeExerciseDbRawUrl}{imgs[0].GetString()}"
                        : null;

                    var equipment = elem.TryGetProperty("equipment", out var eq) ? eq.GetString() : null;

                    exercises.Add(Exercise.Create(
                        name: name,
                        muscleGroup: NormalizeMuscleGroup(primaryMuscles),
                        description: description,
                        animationUrl: imageUrl,
                        equipment: equipment
                        )
                    );
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

        private static string NormalizeMuscleGroup(string rawMuscle) =>
            rawMuscle.ToLowerInvariant().Trim() switch
            {
                var m when m.Contains("chest") || m.Contains("pectoral") => "Pecho",
                var m when m.Contains("lat") || m.Contains("back") => "Espalda",
                var m when m.Contains("quad") || m.Contains("hamstring") || m.Contains("leg") || m.Contains("calf") => "Piernas",
                var m when m.Contains("shoulder") || m.Contains("delt") => "Hombros",
                var m when m.Contains("bicep") || m.Contains("tricep") || m.Contains("arm") => "Brazos",
                var m when m.Contains("abdom") || m.Contains("core") => "Core",
                _ => "Cuerpo Completo"
            };
    }
}
