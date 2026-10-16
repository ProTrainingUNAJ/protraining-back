namespace Infrastructure.Services.Catalog;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Servicio de catálogo de ejercicios para el asistente.
/// Mantiene en memoria el catálogo de ejercicios oficiales del club con identificadores determinísticos estables,
/// permitiendo verificación instantánea por ID y resolución semántica por nombre y alias comunes.
/// </summary>
public sealed class ExerciseCatalogService : IExerciseCatalogService
{
    private readonly Dictionary<Guid, CatalogExerciseDto> _byId = [];
    private readonly Dictionary<string, CatalogExerciseDto> _byNormalizedName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CatalogExerciseDto> _allExercises = [];
    private readonly ILogger<ExerciseCatalogService> _logger;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "con", "en", "el", "la", "los", "las", "un", "una", "unos", "unas", "para", "por", "al", "del", "y", "o", "no"
    };

    public ExerciseCatalogService(ILogger<ExerciseCatalogService> logger)
    {
        _logger = logger;
        InitializeStandardCatalog();
        TryLoadFromFileSystem();
    }

    public Task<CatalogExerciseDto?> FindByIdAsync(Guid exerciseId, CancellationToken cancellationToken = default)
    {
        _byId.TryGetValue(exerciseId, out var item);
        return Task.FromResult(item);
    }

    public Task<CatalogExerciseDto?> FindByNameAsync(string exerciseName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(exerciseName))
        {
            return Task.FromResult<CatalogExerciseDto?>(null);
        }

        var normalized = NormalizeText(exerciseName);

        // 1. Coincidencia exacta por nombre normalizado
        if (_byNormalizedName.TryGetValue(normalized, out var exactMatch))
        {
            return Task.FromResult<CatalogExerciseDto?>(exactMatch);
        }

        // 2. Coincidencia eliminando contenido entre paréntesis (ej. "(Back Squat)", "(TRX)")
        var withoutParentheses = RemoveParentheses(normalized);
        if (_byNormalizedName.TryGetValue(withoutParentheses, out var cleanMatch))
        {
            return Task.FromResult<CatalogExerciseDto?>(cleanMatch);
        }

        // 3. Coincidencia por alias
        foreach (var item in _allExercises)
        {
            if (item.Aliases == null) continue;
            foreach (var alias in item.Aliases)
            {
                var normAlias = NormalizeText(alias);
                if (normAlias == normalized || normAlias == withoutParentheses)
                {
                    return Task.FromResult<CatalogExerciseDto?>(item);
                }
            }
        }

        // 4. Coincidencia por conjunto de términos significativos (excluyendo conectores y palabras vacías)
        var searchMeaningful = withoutParentheses
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !StopWords.Contains(t) && t.Length > 2)
            .ToList();

        if (searchMeaningful.Count == 0)
        {
            return Task.FromResult<CatalogExerciseDto?>(null);
        }

        CatalogExerciseDto? bestMatch = null;
        var highestScore = 0.0;

        foreach (var item in _allExercises)
        {
            var itemNorm = NormalizeText(item.Name);
            var itemMeaningful = itemNorm
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => !StopWords.Contains(t) && t.Length > 2)
                .ToList();

            if (itemMeaningful.Count == 0) continue;

            var common = searchMeaningful.Count(st => itemMeaningful.Any(it => it.Equals(st, StringComparison.OrdinalIgnoreCase)));
            if (common == 0) continue;

            var recall = (double)common / searchMeaningful.Count;
            var precision = (double)common / itemMeaningful.Count;
            var f1 = 2 * (precision * recall) / (precision + recall);

            // Requerimos que al menos el 75% de los términos clave coincidan
            if (recall >= 0.75 && precision >= 0.60 && f1 > highestScore)
            {
                highestScore = f1;
                bestMatch = item;
            }
        }

        return Task.FromResult(bestMatch);
    }

    public Task<IReadOnlyList<CatalogExerciseDto>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return Task.FromResult<IReadOnlyList<CatalogExerciseDto>>(_allExercises);
        }

        var normalized = NormalizeText(searchTerm);
        var results = _allExercises
            .Where(e => NormalizeText(e.Name).Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult<IReadOnlyList<CatalogExerciseDto>>(results);
    }

    public Task<IReadOnlyList<CatalogExerciseDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<CatalogExerciseDto>>(_allExercises);
    }

    private void AddExercise(string name, string muscleGroup, string equipment, params string[] aliases)
    {
        var id = GenerateDeterministicGuid(name);
        var dto = new CatalogExerciseDto(id, name, muscleGroup, equipment, aliases);

        _byId[id] = dto;
        var normName = NormalizeText(name);
        _byNormalizedName[normName] = dto;
        _allExercises.Add(dto);
    }

    private void InitializeStandardCatalog()
    {
        // Piernas
        AddExercise("Sentadilla trasera con barra", "Piernas", "Barra", "Sentadilla con barra", "Back Squat", "Sentadilla trasera", "Sentadilla completa con barra");
        AddExercise("Sentadilla frontal con barra", "Piernas", "Barra", "Front Squat", "Sentadilla frontal");
        AddExercise("Sentadilla búlgara con mancuernas", "Piernas", "Mancuernas", "Sentadilla búlgara", "Bulgarian Split Squat");
        AddExercise("Sentadilla Goblet con mancuerna", "Piernas", "Mancuerna", "Goblet Squat", "Sentadilla Goblet");
        AddExercise("Peso muerto rumano con barra", "Piernas", "Barra", "Peso muerto rumano", "Romanian Deadlift", "RDL");
        AddExercise("Peso muerto convencional", "Piernas", "Barra", "Peso muerto", "Deadlift", "Peso muerto con barra");
        AddExercise("Prensa de piernas 45°", "Piernas", "Máquina", "Prensa de piernas", "Prensa 45", "Leg Press");
        AddExercise("Curl femoral acostado en máquina", "Piernas", "Máquina", "Curl femoral", "Hamstring Curl");
        AddExercise("Extensiones de cuádriceps en máquina", "Piernas", "Máquina", "Sillón de cuadriceps", "Leg Extension");
        AddExercise("Elevación de talones de pie", "Piernas", "Máquina", "Gemelos de pie", "Calf Raise");
        AddExercise("Empuje de cadera (Hip Thrust) con barra", "Piernas", "Barra", "Hip Thrust", "Puente de glúteos con barra");
        AddExercise("Zancadas caminando continuas", "Piernas", "Mancuernas", "Zancadas con mancuernas", "Walking Lunges", "Estocadas");
        AddExercise("Subidas al cajón dinámicas (Step-ups)", "Piernas", "Cajón", "Step-ups", "Subidas al cajón");

        // Pecho
        AddExercise("Press de banca plano con barra", "Pecho", "Barra", "Press de banca", "Bench Press", "Press plano");
        AddExercise("Press de banca inclinado con mancuernas", "Pecho", "Mancuernas", "Press inclinado", "Incline Dumbbell Press");
        AddExercise("Press de banca plano con mancuernas", "Pecho", "Mancuernas", "Press con mancuernas");
        AddExercise("Aperturas con mancuernas en banco plano", "Pecho", "Mancuernas", "Aperturas planas", "Chest Flyes");
        AddExercise("Flexiones de brazos tradicionales", "Pecho", "Peso corporal", "Flexiones de brazos", "Push-ups", "Lagartijas");
        AddExercise("Fondos en paralelas para pecho", "Pecho", "Paralelas", "Dips", "Fondos");

        // Espalda
        AddExercise("Remo con barra Pendlay", "Espalda", "Barra", "Remo Pendlay", "Pendlay Row");
        AddExercise("Remo con barra inclinado", "Espalda", "Barra", "Remo con barra", "Barbell Row");
        AddExercise("Remo unilateral con mancuerna", "Espalda", "Mancuerna", "Remo con mancuerna", "Dumbbell Row");
        AddExercise("Remo en polea baja con agarre neutro", "Espalda", "Polea", "Remo en polea", "Seated Cable Row");
        AddExercise("Dominadas con lastre o agarre prono", "Espalda", "Barra fija", "Dominadas", "Pull-ups");
        AddExercise("Jalón al pecho en polea alta", "Espalda", "Polea", "Jalón al pecho", "Lat Pulldown");
        AddExercise("Tracción facial con banda elástica (Face Pull)", "Espalda", "Banda elástica", "Face Pull");

        // Hombros y Brazos
        AddExercise("Press militar de pie con barra", "Hombros", "Barra", "Press militar", "Overhead Press", "OHP");
        AddExercise("Elevaciones laterales con mancuernas", "Hombros", "Mancuernas", "Vuelos laterales", "Lateral Raises");
        AddExercise("Pájaros con mancuernas", "Hombros", "Mancuernas", "Posterior con mancuernas");
        AddExercise("Curl de bíceps con barra Z", "Brazos", "Barra", "Curl con barra", "Bicep Curl");
        AddExercise("Extensiones de tríceps en polea alta", "Brazos", "Polea", "Tríceps en polea", "Tricep Pushdown");

        // Core y Zona Media
        AddExercise("Plancha frontal isométrica", "Core", "Peso corporal", "Plancha frontal", "Plank", "Plancha");
        AddExercise("Plancha isométrica con sobrecarga", "Core", "Disco", "Plancha con disco");
        AddExercise("Rueda abdominal (Ab rollouts)", "Core", "Rueda abdominal", "Ab Wheel", "Rueda abdominal");
        AddExercise("Press Pallof con banda elástica", "Core", "Banda elástica", "Press Pallof", "Pallof Press");
        AddExercise("Deadbug controlado con respiración diafragmática", "Core", "Peso corporal", "Deadbug", "Bicho muerto");

        // Pliometría y Potencia
        AddExercise("Saltos al cajón pliométricos (Box Jumps)", "Potencia", "Cajón", "Box Jumps", "Saltos al cajón");
        AddExercise("Swings con pesa rusa (Kettlebell)", "Potencia", "Pesa rusa", "Kettlebell Swing", "Swings");
        AddExercise("Cargada de potencia colgante (Hang Power Clean)", "Potencia", "Barra", "Hang Clean", "Power Clean");
        AddExercise("Push Press con barra", "Potencia", "Barra", "Push Press");
    }

    private void TryLoadFromFileSystem()
    {
        try
        {
            // Intentamos localizar exercises.json en ubicaciones relativas conocidas
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Data", "exercises.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "Data", "exercises.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "entrenamiento", "API", "Data", "exercises.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "services", "entrenamiento", "API", "Data", "exercises.json")
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    var jsonContent = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(jsonContent);

                    var countAdded = 0;
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        if (elem.TryGetProperty("name", out var nameProp))
                        {
                            var name = nameProp.GetString();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                var norm = NormalizeText(name);
                                if (!_byNormalizedName.ContainsKey(norm))
                                {
                                    var muscle = elem.TryGetProperty("primaryMuscles", out var pm) && pm.GetArrayLength() > 0
                                        ? pm[0].GetString() ?? "General"
                                        : "General";
                                    var equipment = elem.TryGetProperty("equipment", out var eq)
                                        ? eq.GetString() ?? "General"
                                        : "General";

                                    AddExercise(name, muscle, equipment);
                                    countAdded++;
                                }
                            }
                        }
                    }

                    _logger.LogInformation("Cargados {Count} ejercicios adicionales desde '{Path}'. Total en catálogo: {Total}.", countAdded, path, _allExercises.Count);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se cargaron ejercicios externos adicionales desde el sistema de archivos.");
        }
    }

    public static Guid GenerateDeterministicGuid(string input)
    {
        var normalized = NormalizeText(input);
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = MD5.HashData(bytes);
        return new Guid(hash);
    }

    public static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString()
            .Normalize(NormalizationForm.FormC)
            .Trim()
            .ToLowerInvariant();
    }

    private static string RemoveParentheses(string text)
    {
        var open = text.IndexOf('(');
        var close = text.IndexOf(')');
        if (open >= 0 && close > open)
        {
            var cleaned = text.Remove(open, close - open + 1);
            return cleaned.Trim();
        }
        return text.Trim();
    }
}
