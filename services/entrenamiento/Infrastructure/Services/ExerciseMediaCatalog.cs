using System.Text.Json;
using Application.DTOs.Exercise;
using Application.Interfaces.Services;

namespace Infrastructure.Services;

// Lee la tabla de cruce API/Data/exercise-media.json una sola vez (KAN-549).
// La tabla guarda solo ids y URLs: las imágenes y los datos siguen en los repos de cada fuente,
// así no republicamos el dataset de RepDB (su licencia lo prohíbe y este repo es público).
// Orden de preferencia: RepDB (dibujo, dos poses) → ExerciseDB (GIF) → Everkinetic (dibujo de línea).
public sealed class ExerciseMediaCatalog : IExerciseMediaCatalog
{
    public static readonly ExerciseMediaCatalog Empty = new(new Dictionary<string, IReadOnlyList<ExerciseMediaDto>>());

    private const string CatalogImagesMarker = "/exercises/";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IReadOnlyDictionary<string, IReadOnlyList<ExerciseMediaDto>> mediaByCatalogId;

    private ExerciseMediaCatalog(IReadOnlyDictionary<string, IReadOnlyList<ExerciseMediaDto>> mediaByCatalogId)
    {
        this.mediaByCatalogId = mediaByCatalogId;
    }

    // Si el archivo no existe, el catálogo queda vacío: los ejercicios se ven con sus fotos de siempre.
    public static ExerciseMediaCatalog FromFile(string path)
    {
        if (!File.Exists(path))
            return Empty;

        return FromJson(File.ReadAllText(path));
    }

    public static ExerciseMediaCatalog FromJson(string json)
    {
        var entries = JsonSerializer.Deserialize<List<MediaEntry>>(json, JsonOptions) ?? [];

        var media = new Dictionary<string, IReadOnlyList<ExerciseMediaDto>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.ExerciseId))
                continue;

            var options = new List<ExerciseMediaDto>();

            if (entry.RepDb?.Frames is { Count: > 0 } repDbFrames)
            {
                options.Add(new ExerciseMediaDto(
                    "repdb", "frames", repDbFrames,
                    "Exercise data by RepDB (repdb.co)", "https://repdb.co", "RepDB Free Tier"));
            }

            if (!string.IsNullOrWhiteSpace(entry.ExerciseDb?.GifUrl))
            {
                options.Add(new ExerciseMediaDto(
                    "exercisedb", "gif", [entry.ExerciseDb.GifUrl],
                    "ExerciseDB", "https://exercisedb.dev", "Uso académico, enlazado sin redistribuir"));
            }

            if (entry.Everkinetic?.Frames is { Count: > 0 } everkineticFrames)
            {
                options.Add(new ExerciseMediaDto(
                    "everkinetic", "frames", everkineticFrames,
                    "Everkinetic", "https://github.com/everkinetic/data", "CC BY-SA 4.0"));
            }

            if (options.Count > 0)
                media[entry.ExerciseId] = options;
        }

        return new ExerciseMediaCatalog(media);
    }

    public IReadOnlyList<ExerciseMediaDto> GetForCatalogImages(IReadOnlyList<string> catalogImages)
    {
        var catalogId = CatalogIdFromImages(catalogImages);
        if (catalogId is null)
            return [];

        return mediaByCatalogId.TryGetValue(catalogId, out var media) ? media : [];
    }

    // Las fotos del catálogo son ".../exercises/{id de free-exercise-db}/0.jpg": de ahí sale el id del cruce.
    private static string? CatalogIdFromImages(IReadOnlyList<string> catalogImages)
    {
        var image = catalogImages.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(image))
            return null;

        var markerIndex = image.IndexOf(CatalogImagesMarker, StringComparison.Ordinal);
        var path = markerIndex >= 0 ? image[(markerIndex + CatalogImagesMarker.Length)..] : image;

        var slashIndex = path.IndexOf('/');
        return slashIndex > 0 ? path[..slashIndex] : null;
    }

    private sealed class MediaEntry
    {
        public string ExerciseId { get; set; } = string.Empty;
        public FramesSource? RepDb { get; set; }
        public GifSource? ExerciseDb { get; set; }
        public FramesSource? Everkinetic { get; set; }
    }

    private sealed class FramesSource
    {
        public List<string>? Frames { get; set; }
    }

    private sealed class GifSource
    {
        public string? GifUrl { get; set; }
    }
}
