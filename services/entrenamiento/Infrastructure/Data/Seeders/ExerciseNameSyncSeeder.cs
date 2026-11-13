using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Infrastructure.Data.Seeders;

// Sincroniza los nombres del catálogo con Data/exercises.json (KAN-554). El catálogo se carga una
// sola vez, con la base vacía; si después se corrige una traducción en el JSON (por ejemplo, dos
// ejercicios distintos que habían quedado con el mismo nombre), este paso la aplica en las bases que
// ya lo tenían. Cada ejercicio se reconoce por el id original de free-exercise-db, que está en la
// ruta de sus fotos (".../exercises/Dumbbell_Floor_Press/0.jpg"). Corre antes de agrupar variantes.
public class ExerciseNameSyncSeeder
{
    private const string FileName = "exercises.json";
    private const string ImagesFolder = "/exercises/";

    public static async Task SeedAsync(AppDbContext context, string dataDirectoryPath, ILogger logger)
    {
        var jsonFilePath = Path.Combine(dataDirectoryPath, FileName);
        if (!File.Exists(jsonFilePath))
        {
            logger.LogWarning("No se encontró el catálogo de ejercicios en {Path}", jsonFilePath);
            return;
        }

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonFilePath));
        var namesBySourceId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var elem in doc.RootElement.EnumerateArray())
        {
            if (elem.TryGetProperty("id", out var id) && elem.TryGetProperty("name", out var name)
                && id.GetString() is { Length: > 0 } sourceId && name.GetString() is { Length: > 0 } sourceName)
            {
                namesBySourceId[sourceId] = sourceName.Trim();
            }
        }

        var catalog = await context.Exercises
            .IgnoreQueryFilters()
            .Where(e => e.PfId == Guid.Empty && e.DeletedAt == null)
            .ToListAsync();

        var renamed = 0;
        foreach (var exercise in catalog)
        {
            var sourceId = SourceIdFromImages(exercise.Images);
            if (sourceId is null || !namesBySourceId.TryGetValue(sourceId, out var expectedName))
                continue;

            if (exercise.Name != expectedName)
            {
                logger.LogInformation("Catálogo: {Old} pasa a llamarse {New}.", exercise.Name, expectedName);
                exercise.Rename(expectedName);
                renamed++;
            }
        }

        if (renamed > 0)
        {
            await context.SaveChangesAsync();
        }
    }

    // ".../free-exercise-db/main/exercises/Dumbbell_Floor_Press/0.jpg" → "Dumbbell_Floor_Press"
    public static string? SourceIdFromImages(IEnumerable<string> images)
    {
        foreach (var url in images)
        {
            var start = url.IndexOf(ImagesFolder, StringComparison.Ordinal);
            if (start < 0)
                continue;

            start += ImagesFolder.Length;
            var end = url.IndexOf('/', start);
            if (end > start)
                return url[start..end];
        }

        return null;
    }
}
