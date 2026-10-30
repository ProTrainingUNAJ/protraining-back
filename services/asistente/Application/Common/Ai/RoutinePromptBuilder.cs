namespace Application.Common.Ai;

using System.Text;
using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;
using Domain.Common;

/// <summary>
/// Constructor centralizado de prompts para el módulo de IA del Asistente (#087 · KAN-382 / KAN-407 / KAN-563).
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
    /// Construye el prompt para la generación de borradores de planificación multisesión (KAN-381 / KAN-382 / KAN-383 / KAN-407 / KAN-563).
    /// Instruye explícitamente a la IA a usar únicamente los IDs provistos en el catálogo y a respetar
    /// las directivas de periodización competitiva pre-partido (D-1 <= 60 %, D-2 <= 75 %, D sin pesas).
    /// </summary>
    public static string BuildPlanDraftPrompt(
        CreatePlanDraftRequestDto request,
        IReadOnlyList<CatalogExerciseDto> availableExercises,
        IReadOnlyList<DateOnly>? matchDates = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert strength and conditioning AI assistant for ProTraining coaches.");
        sb.AppendLine("Generate a structured multi-week training plan draft.");
        sb.AppendLine($"Goal: {request.Goal}");
        sb.AppendLine($"Weeks: {request.Weeks}");
        sb.AppendLine($"Days per week: {request.DaysPerWeek}");
        if (request.TrainingDays is { Count: > 0 })
        {
            sb.AppendLine($"Preferred training days: {string.Join(", ", request.TrainingDays)}");
        }
        if (request.StartDate.HasValue)
        {
            sb.AppendLine($"Start date: {request.StartDate.Value:yyyy-MM-dd}");
        }
        sb.AppendLine();

        // KAN-407: Periodización competitiva pre-partido
        if (request.StartDate.HasValue && matchDates is { Count: > 0 })
        {
            var startDate = request.StartDate.Value;
            var endDate = startDate.AddDays(request.Weeks * 7 - 1);
            var relevantMatches = matchDates
                .Where(m => m >= startDate && m <= endDate)
                .OrderBy(m => m)
                .ToList();

            if (relevantMatches.Count > 0)
            {
                sb.AppendLine("COMPETITIVE MATCH SCHEDULE (CALENDAR):");
                foreach (var match in relevantMatches)
                {
                    var daysDiff = match.DayNumber - startDate.DayNumber;
                    var weekNumber = (daysDiff / 7) + 1;
                    var dayName = match.DayOfWeek.ToString();
                    sb.AppendLine($"- Week {weekNumber}, {dayName} ({match:yyyy-MM-dd}): Official Match");
                }
                sb.AppendLine();
                sb.AppendLine("MANDATORY MATCHDAY PERIODIZATION AND TAPERING RULES:");
                sb.AppendLine("1. MATCH DAY (Day D): Do NOT schedule any strength or gym sessions on the day of the match.");
                sb.AppendLine($"2. DAY BEFORE MATCH (Day D-1): Target percentage MUST NOT exceed {MatchDayLoadRules.MaxPercentageOneDayBeforeMatch:0}% for any exercise (low neuromuscular load).");
                sb.AppendLine($"3. TWO DAYS BEFORE MATCH (Day D-2): Target percentage MUST NOT exceed {MatchDayLoadRules.MaxPercentageTwoDaysBeforeMatch:0}% for any exercise (moderate load).");
                sb.AppendLine("4. Rest of the days: Normal training intensities according to the goal.");
                sb.AppendLine();
            }
        }

        sb.AppendLine("OFFICIAL CATALOG EXERCISES (USE ONLY THESE IDS):");
        foreach (var ex in availableExercises)
        {
            var groupInfo = !string.IsNullOrWhiteSpace(ex.MuscleGroup) ? $" | Muscle: {ex.MuscleGroup}" : "";
            sb.AppendLine($"- [Id: {ex.Id}] {ex.Name}{groupInfo}");
        }
        sb.AppendLine();

        sb.AppendLine("STRICT DIRECTIVES:");
        sb.AppendLine("1. USE ONLY THE IDS FROM THE LIST ABOVE. DO NOT INVENT EXERCISE IDS.");
        sb.AppendLine("2. Every exercise in the plan MUST have an 'exerciseId' matching an ID from the list above.");
        sb.AppendLine("3. Sets must be an integer between 1 and 10.");
        sb.AppendLine("4. Reps must be an integer between 1 and 30.");
        sb.AppendLine("5. Target percentage must be a number between 0 and 100 or null.");
        sb.AppendLine("6. Output ONLY valid JSON in English, matching this EXACT schema with NO markdown fences, NO extra text:");
        sb.AppendLine("""
            {
              "name": "Mesocycle Name",
              "objective": "Training cycle objective",
              "justificationSummary": "Scientific rationale and periodization explanation",
              "weeks": [
                {
                  "weekNumber": 1,
                  "sessions": [
                    {
                      "name": "Lower Body Strength",
                      "dayOfWeek": "Monday",
                      "order": 1,
                      "coachNotes": "Focus on explosive drive",
                      "exercises": [
                        {
                          "exerciseId": "exact-catalog-guid-here",
                          "order": 1,
                          "sets": 4,
                          "reps": 6,
                          "targetPercentage": 80.0
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """);

        return sb.ToString();
    }

    /// <summary>
    /// Extrae los grupos musculares clave del texto del objetivo de entrenamiento (#158 · KAN-563).
    /// </summary>
    public static IReadOnlyList<string> ExtractMuscleGroups(string? goal)
    {
        if (string.IsNullOrWhiteSpace(goal))
            return [];

        var text = goal.Trim().ToLowerInvariant()
            .Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u");
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Piernas / Tren Inferior
        if (text.Contains("pierna") || text.Contains("cuadriceps") ||
            text.Contains("isquio") || text.Contains("gluteo") ||
            text.Contains("gemelo") || text.Contains("pantorrilla") ||
            text.Contains("tren inferior") || text.Contains("lower body") ||
            text.Contains("sentadilla") || text.Contains("peso muerto") ||
            text.Contains("prensa") || text.Contains("estocada") || text.Contains("zancada"))
        {
            groups.Add("Piernas");
        }

        // Pecho / Empuje
        if (text.Contains("pecho") || text.Contains("pectoral") ||
            text.Contains("empuje") || text.Contains("push") ||
            text.Contains("press de banca") || text.Contains("press banca") ||
            text.Contains("fondo"))
        {
            groups.Add("Pecho");
        }

        // Espalda / Tracción
        if (text.Contains("espalda") || text.Contains("dorsal") ||
            text.Contains("traccion") || text.Contains("pull") ||
            text.Contains("remo") || text.Contains("dominada") ||
            text.Contains("lumbar") || text.Contains("trapecio"))
        {
            groups.Add("Espalda");
        }

        // Hombros
        if (text.Contains("hombro") || text.Contains("deltoides") || text.Contains("press militar"))
        {
            groups.Add("Hombros");
        }

        // Brazos
        if (text.Contains("brazo") || text.Contains("biceps") || text.Contains("triceps"))
        {
            groups.Add("Brazos");
        }

        // Core / Abdomen
        if (text.Contains("core") || text.Contains("abdominal") ||
            text.Contains("abdomen") || text.Contains("zona media") || text.Contains("plancha"))
        {
            groups.Add("Core");
        }

        return groups.ToList();
    }

    /// <summary>
    /// Filtra y prioriza los ejercicios del catálogo oficial según el objetivo deportivo solicitado (#158 · KAN-563).
    /// Si el objetivo enfatiza grupos específicos (ej. piernas), se priorizan ejercicios de esos grupos.
    /// Si el objetivo es general (ej. fuerza general, pretemporada), distribuye equitativamente básicos de cada grupo.
    /// </summary>
    public static IReadOnlyList<CatalogExerciseDto> FilterExercisesForGoal(
        IReadOnlyList<CatalogExerciseDto> catalog,
        string? goal,
        int maxExercises = 25)
    {
        if (catalog == null || catalog.Count == 0)
            return [];

        if (maxExercises <= 0)
            maxExercises = 25;

        var extractedGroups = ExtractMuscleGroups(goal);

        if (extractedGroups.Count > 0)
        {
            return FilterRelevantExercises(catalog, extractedGroups, maxExercises);
        }

        // Objetivo general: distribuir una mezcla balanceada de básicos de cada grupo
        var targetGroups = new[] { "Piernas", "Pecho", "Espalda", "Hombros", "Core", "Brazos" };
        var selected = new List<CatalogExerciseDto>();
        var selectedIds = new HashSet<Guid>();

        var perGroupLimit = Math.Max(2, maxExercises / targetGroups.Length);
        foreach (var group in targetGroups)
        {
            var groupExercises = catalog
                .Where(e => !selectedIds.Contains(e.Id) &&
                            e.MuscleGroup != null &&
                            e.MuscleGroup.Trim().Equals(group, StringComparison.OrdinalIgnoreCase))
                .Take(perGroupLimit);

            foreach (var ex in groupExercises)
            {
                if (selected.Count >= maxExercises)
                    break;

                selected.Add(ex);
                selectedIds.Add(ex.Id);
            }

            if (selected.Count >= maxExercises)
                break;
        }

        // Si todavía quedan cupos, completar con los restantes del catálogo
        if (selected.Count < maxExercises)
        {
            foreach (var ex in catalog)
            {
                if (!selectedIds.Contains(ex.Id))
                {
                    selected.Add(ex);
                    selectedIds.Add(ex.Id);
                    if (selected.Count >= maxExercises)
                        break;
                }
            }
        }

        return selected;
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
