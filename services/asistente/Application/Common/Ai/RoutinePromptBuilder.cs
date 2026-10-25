namespace Application.Common.Ai;

using System.Text;
using Application.DTOs.Ai;

/// <summary>
/// Constructor centralizado de prompts para el módulo de IA del Asistente (#087 · KAN-382).
/// Inyecta los ejercicios disponibles del catálogo oficial, reglas de privacidad y directivas JSON.
/// </summary>
public static class RoutinePromptBuilder
{
    public static string BuildPrompt(
        RoutineSuggestionRequestDto request,
        IReadOnlyList<CatalogExerciseDto>? availableExercises = null)
    {
        var muscles = request.MuscleGroupEmphasis is { Count: > 0 }
            ? string.Join(", ", request.MuscleGroupEmphasis)
            : "General";

        var sb = new StringBuilder();
        sb.AppendLine("Eres un asistente inteligente de entrenamiento para preparadores físicos de ProTraining.");
        sb.AppendLine($"Diseña una propuesta de rutina para el atleta con identificador anónimo '{request.AthleteId}'.");
        sb.AppendLine($"Objetivo de entrenamiento: {request.Goal}");
        sb.AppendLine($"Énfasis muscular solicitado: {muscles}");
        sb.AppendLine();

        var exercises = availableExercises ?? request.AvailableExercises;
        if (exercises is { Count: > 0 })
        {
            sb.AppendLine("EJERCICIOS DISPONIBLES EN LA BIBLIOTECA OFICIAL DEL CLUB:");
            foreach (var ex in exercises)
            {
                var groupInfo = !string.IsNullOrWhiteSpace(ex.MuscleGroup) ? $", Grupo: {ex.MuscleGroup}" : "";
                var equipInfo = !string.IsNullOrWhiteSpace(ex.Equipment) ? $", Equipo: {ex.Equipment}" : "";
                sb.AppendLine($"- [Id: {ex.Id}] {ex.Name}{groupInfo}{equipInfo}");
            }
            sb.AppendLine();
            sb.AppendLine("DIRECTIVAS DEL CATÁLOGO:");
            sb.AppendLine("1. Utiliza preferentemente los ejercicios disponibles listados arriba.");
            sb.AppendLine("2. Para cada ejercicio que elijas del catálogo, debes incluir su 'ejercicioId' exacto y su 'ejercicioNombre' tal como aparece en la lista.");
            sb.AppendLine("3. Si consideras indispensable sugerir una variante fuera de catálogo, indícala con 'ejercicioId': null y su 'ejercicioNombre'.");
        }
        else
        {
            sb.AppendLine("DIRECTIVAS:");
            sb.AppendLine("1. Propón ejercicios metodológicamente coherentes con el objetivo y el grupo muscular solicitado.");
        }

        sb.AppendLine();
        sb.AppendLine("REGLAS ESTRICTAS DE PRIVACIDAD Y FORMATO:");
        sb.AppendLine("1. PRIVACIDAD: No incluyas nombres de personas.");
        sb.AppendLine("2. Responde ÚNICAMENTE con un JSON que tenga esta estructura exacta:");
        sb.AppendLine("""
            {
              "tituloPropuesto": "Título de la rutina",
              "resumenJustificacion": "Explicación breve del criterio y adaptaciones aplicadas",
              "ejercicios": [
                {
                  "ejercicioId": "11111111-1111-1111-1111-111111111111",
                  "ejercicioNombre": "Nombre del ejercicio",
                  "series": 3,
                  "repeticiones": 10,
                  "porcentajeSugerido": 70.0
                }
              ]
            }
            """);

        return sb.ToString();
    }

    /// <summary>
    /// Filtra y prioriza los ejercicios del catálogo más relevantes según el énfasis muscular solicitado.
    /// Limita el total (por defecto 20) para mantener el prompt ágil y conciso para modelos compactos como Llama 3.2 1B.
    /// </summary>
    public static IReadOnlyList<CatalogExerciseDto> FilterRelevantExercises(
        IReadOnlyList<CatalogExerciseDto> catalog,
        IReadOnlyList<string>? muscleGroupEmphasis,
        int maxExercises = 20)
    {
        if (catalog == null || catalog.Count == 0)
        {
            return [];
        }

        if (maxExercises <= 0)
        {
            maxExercises = 20;
        }

        if (muscleGroupEmphasis == null || muscleGroupEmphasis.Count == 0)
        {
            return catalog.Take(maxExercises).ToList();
        }

        var normalizedEmphasis = muscleGroupEmphasis
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim().ToLowerInvariant())
            .ToHashSet();

        if (normalizedEmphasis.Count == 0)
        {
            return catalog.Take(maxExercises).ToList();
        }

        var matching = new List<CatalogExerciseDto>();
        var remaining = new List<CatalogExerciseDto>();

        foreach (var exercise in catalog)
        {
            var group = exercise.MuscleGroup?.Trim().ToLowerInvariant();
            var name = exercise.Name?.Trim().ToLowerInvariant();

            var isMatch = normalizedEmphasis.Any(e =>
                (!string.IsNullOrEmpty(group) && (group.Contains(e) || e.Contains(group))) ||
                (!string.IsNullOrEmpty(name) && name.Contains(e)));

            if (isMatch)
            {
                matching.Add(exercise);
            }
            else
            {
                remaining.Add(exercise);
            }
        }

        if (matching.Count >= maxExercises)
        {
            return matching.Take(maxExercises).ToList();
        }

        var combined = new List<CatalogExerciseDto>(matching);
        foreach (var ex in remaining)
        {
            if (combined.Count >= maxExercises)
            {
                break;
            }
            combined.Add(ex);
        }

        return combined;
    }
}
