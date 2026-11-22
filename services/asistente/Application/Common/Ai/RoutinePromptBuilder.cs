namespace Application.Common.Ai;

using System.Text;
using Application.Common.Helpers;
using Application.DTOs.Adaptation;
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

        AppendAdaptationContext(sb, request.AdaptationContext);

        AppendPfDecisionContext(sb, request.PreviousPfDecisions);

        var exercises = availableExercises ?? request.AvailableExercises;
        var maxExercises = GoalConstraintExtractor.ExtractMaxExercisesPerSession(request.Goal);
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
            if (maxExercises.HasValue)
            {
                sb.AppendLine($"4. RESTRICCIÓN OBLIGATORIA DEL PF: Incluye como máximo {maxExercises.Value} ejercicios en total.");
            }
        }
        else
        {
            sb.AppendLine("DIRECTIVAS:");
            sb.AppendLine("1. Propón ejercicios metodológicamente coherentes con el objetivo y el grupo muscular solicitado.");
            if (maxExercises.HasValue)
            {
                sb.AppendLine($"2. RESTRICCIÓN OBLIGATORIA DEL PF: Incluye como máximo {maxExercises.Value} ejercicios en total.");
            }
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
    /// Agrega al prompt las lesiones activas, los récords vigentes y las restricciones con sus sustitutos autorizados.
    /// Solo viajan zonas, ejercicios y porcentajes: nunca texto libre de lesiones ni datos personales (KAN-600).
    /// </summary>
    public static void AppendAdaptationContext(StringBuilder sb, AthleteAdaptationContextDto? context)
    {
        if (context is null)
            return;

        sb.AppendLine("CONTEXTO MÉDICO-DEPORTIVO DEL ATLETA:");
        if (context.ActiveInjuries.Count == 0)
            sb.AppendLine("- No hay lesiones activas informadas.");
        else
            foreach (var injury in context.ActiveInjuries)
            {
                var zone = string.IsNullOrWhiteSpace(injury.Zone) ? "zona no informada" : injury.Zone.Trim();
                var status = string.IsNullOrWhiteSpace(injury.Status) ? "estado no informado" : injury.Status.Trim();
                sb.AppendLine($"- Lesión activa en {zone} (estado: {status}; severidad: no informada).");
            }

        if (context.CurrentRecords.Count == 0)
        {
            sb.AppendLine("- No hay récords vigentes: no inventes cargas ni porcentajes basados en 1RM.");
        }
        else
        {
            foreach (var record in context.CurrentRecords)
                sb.AppendLine($"- Récord vigente: {record.ExerciseName}, 1RM {record.WeightOneRepMaxKg:0.##} kg ({record.Origin}).");
        }

        if (context.Restrictions.Count > 0)
        {
            sb.AppendLine("RESTRICCIONES Y SUSTITUTOS AUTORIZADOS:");
            foreach (var restriction in context.Restrictions)
            {
                var substitutes = restriction.Substitutes.Count == 0
                    ? "sin sustituto autorizado"
                    : string.Join(", ", restriction.Substitutes.Select(s => $"{s.Name} [{s.ExerciseId}]"));
                sb.AppendLine($"- No usar ejercicio {restriction.ExerciseId} por {restriction.Zone}: {substitutes}.");
            }
        }

        sb.AppendLine("REGLAS DE ADAPTACIÓN:");
        sb.AppendLine("1. No propongas ejercicios restringidos.");
        sb.AppendLine("2. Si debes reemplazar un ejercicio restringido, usa únicamente el sustituto autorizado indicado.");
        sb.AppendLine("3. Explica la sustitución y avisa al preparador físico en el resumen.");
        sb.AppendLine("4. Distingue los récords estimados de los testeados y no inventes datos faltantes.");
        sb.AppendLine();
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

        AppendPfDecisionContext(sb, request.PreviousPfDecisions);

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
        sb.AppendLine("6. Output ONLY valid JSON matching this EXACT schema with NO markdown fences, NO extra text. Keep the JSON keys and 'dayOfWeek' in English.");
        // El PF lee el nombre, el objetivo, la justificación y las notas: van en español (KAN-578)
        sb.AppendLine("7. Write every text value the coach reads ('name', 'objective', 'justificationSummary', session 'name' and 'coachNotes') in Spanish from Argentina, using 'vos' (e.g. 'Fuerza de piernas', 'Activá glúteos antes de sentadilla'). Never write those values in English.");
        // Sin esta regla los modelos chicos copian el ejemplo (un ejercicio) y arman sesiones de 1 o 2 ejercicios
        var requestedMaxExercises = GoalConstraintExtractor.ExtractMaxExercisesPerSession(request.Goal);
        if (requestedMaxExercises.HasValue)
        {
            sb.AppendLine($"8. MANDATORY COACH CONSTRAINT: The coach explicitly requested at most {requestedMaxExercises.Value} exercises per session/day. Every session MUST include at most {requestedMaxExercises.Value} different exercises from the list above (NEVER exceed {requestedMaxExercises.Value} exercises per session): main compound lifts first, then accessory work. Number them with 'order' starting at 1.");
        }
        else
        {
            sb.AppendLine("8. Every session MUST include between 5 and 6 different exercises from the list above (fewer only if the list has fewer): main compound lifts first, then accessory work. Number them with 'order' starting at 1.");
        }
        // El catálogo no tiene ejercicios de entrada en calor: va como texto al principio de la nota de la sesión
        sb.AppendLine("9. Start every session's 'coachNotes' with a concrete warm-up of about 10 minutes, in Spanish ('Entrada en calor (10 min): ...'): joint mobility for the muscles of the day and ramp-up sets at 40-60% before the first heavy lift. Then add the technical note for the session.");
        // El ejemplo de abajo tiene una sola semana y los modelos chicos lo copian: hay que pedir la cantidad exacta
        sb.AppendLine($"10. The 'weeks' array MUST contain exactly {request.Weeks} week objects, with 'weekNumber' from 1 to {request.Weeks}, and each week MUST contain exactly {request.DaysPerWeek} sessions.");
        sb.AppendLine("""
            {
              "name": "Nombre del mesociclo",
              "objective": "Objetivo del ciclo de entrenamiento",
              "justificationSummary": "Fundamento de la periodización",
              "weeks": [
                {
                  "weekNumber": 1,
                  "sessions": [
                    {
                      "name": "Fuerza de tren inferior",
                      "dayOfWeek": "Monday",
                      "order": 1,
                      "coachNotes": "Priorizá la potencia en la subida",
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

    public static string BuildBlockPlanDraftPrompt(
        CreatePlanDraftRequestDto request,
        IReadOnlyList<CatalogExerciseDto> availableExercises,
        IReadOnlyList<DateOnly>? matchDates = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Sos un preparador físico experto para ProTraining.");
        sb.AppendLine("Diseñá una sesión modelo de una semana dividida en bloques. La aplicación calcula la progresión de las semanas siguientes.");
        sb.AppendLine($"Objetivo: {request.Goal}");
        sb.AppendLine($"Días por semana: {request.DaysPerWeek}");
        sb.AppendLine("La respuesta debe contener exactamente una sesión por día solicitado, priorizando los índices del catálogo oficial.");
        sb.AppendLine();

        AppendPfDecisionContext(sb, request.PreviousPfDecisions);
        AppendAdaptationContext(sb, request.AdaptationContext);

        var targetGroups = ExtractMuscleGroups(request.Goal);
        if (targetGroups.Count > 0)
        {
            sb.AppendLine($"ENFOQUE MUSCULAR DEL OBJETIVO: {string.Join(", ", targetGroups)}.");
            sb.AppendLine("DIRECTIVAS DE ESPECIFICIDAD DEPORTIVA:");
            sb.AppendLine("1. Los bloques principales (strengthA y strengthB) y accesorios DEBEN priorizar ejercicios de estos grupos musculares compatibles.");
            sb.AppendLine("2. No mezcles patrones incongruentes (por ejemplo, en objetivos de tren superior no utilices sentadillas ni prensa en los bloques principales).");
            sb.AppendLine();
        }

        var protectedZones = GoalConstraintExtractor.ExtractProtectedZones(request.Goal);
        if (protectedZones.Count > 0)
        {
            var zonesLabel = string.Join(", ", protectedZones);
            sb.AppendLine($"FOCO ARTICULAR / PREVENTIVO EN EL OBJETIVO ({zonesLabel}):");
            sb.AppendLine($"- El preparador físico indicó foco o cuidado específico en {zonesLabel}. Priorizá ejercicios biomecánicamente seguros y estables para {zonesLabel}, evitando sobrecarga articular directa o ejercicios restringidos para esa zona.");
            sb.AppendLine();
        }

        var maxExercisesPerSession = GoalConstraintExtractor.ExtractMaxExercisesPerSession(request.Goal);

        sb.AppendLine("BLOQUES OBLIGATORIOS Y PAUTAS DE CARGA:");
        sb.AppendLine("1. warmUp: 1-2 ejercicios de entrada en calor, movilidad articular o activación neuromuscular. Sin porcentaje de RM.");
        sb.AppendLine("2. strengthA: ejercicio principal multiarticular, 3-5 series de 3-8 repeticiones (intensidad objetivo 75-85% RM).");
        sb.AppendLine("3. strengthB: segundo patrón de fuerza complementario o unilateral, 3-4 series de 5-10 repeticiones (intensidad objetivo 70-80% RM).");
        sb.AppendLine("4. accessories: 1-3 ejercicios analíticos, de aislamiento o zona media/core, 2-4 series de 8-15 repeticiones (sin RM o moderado).");
        if (maxExercisesPerSession.HasValue)
        {
            sb.AppendLine($"RESTRICCIÓN PRIORITARIA DEL PREPARADOR FÍSICO: El objetivo pide como máximo {maxExercisesPerSession.Value} ejercicios por día/sesión.");
            sb.AppendLine($"Usá como máximo {maxExercisesPerSession.Value} ejercicios por sesión en total sumando todos los bloques (warmUp + strengthA + strengthB + accessories <= {maxExercisesPerSession.Value}; por ejemplo: 1 en warmUp, 1 en strengthA, 1 en strengthB y {Math.Max(0, maxExercisesPerSession.Value - 3)} en accessories), sin repetir un ejercicio dentro de la sesión.");
        }
        else
        {
            sb.AppendLine("Usá entre 5 y 6 ejercicios por sesión cuando el catálogo lo permita, sin repetir un ejercicio dentro de la sesión.");
        }
        sb.AppendLine("DIRECTIVA DE CUMPLIMIENTO DEL OBJETIVO: Toda condición específica pedida por el preparador físico en el objetivo (cantidad máxima de ejercicios por día, foco articular, equipamiento o ejercicios puntuales) es OBLIGATORIA y tiene prioridad sobre las pautas generales.");
        sb.AppendLine("Escribí name, objective, justificationSummary y coachNotes en español de Argentina, de forma concreta y técnica.");
        sb.AppendLine("En coachNotes indicá descansos recomendados (ej. 2-3 min en básicos, 60-90 s en accesorios) y foco técnico en fase excéntrica.");
        sb.AppendLine("No calcules progresiones semanales: eso lo hará una regla determinística en C#.");

        if (matchDates is { Count: > 0 })
        {
            sb.AppendLine("Hay partidos oficiales en el calendario. No armes fuerza el día del partido y respetá D-1 <= 60% y D-2 <= 75%.");
        }

        sb.AppendLine();
        sb.AppendLine("CATÁLOGO OFICIAL. El índice empieza en 1:");
        for (var index = 0; index < availableExercises.Count; index++)
        {
            var exercise = availableExercises[index];
            sb.AppendLine($"{index + 1}. {exercise.Name} | grupo: {exercise.MuscleGroup ?? "no informado"} | id: {exercise.Id}");
        }
        sb.AppendLine("Si necesitás proponer un ejercicio que NO está en el catálogo oficial, devolvé \"exerciseIndex\": null junto con \"exerciseName\" y \"muscleGroup\" (usando los mismos nombres de grupo muscular del catálogo). Si el ejercicio SÍ está en el catálogo, usá obligatoriamente su \"exerciseIndex\" y no inventes uno nuevo.");
        sb.AppendLine();
        sb.AppendLine("Respondé únicamente JSON válido con este esquema:");
        sb.AppendLine(BlockPromptSchema.SchemaJson);
        return sb.ToString();
    }

    public static void AppendPfDecisionContext(
        StringBuilder sb,
        IReadOnlyList<PfDecisionContextDto>? decisions)
    {
        if (decisions is not { Count: > 0 })
            return;

        sb.AppendLine("DECISIONES PREVIAS DEL PREPARADOR FÍSICO:");
        sb.AppendLine("Usá estas decisiones como evidencia del criterio del PF. No las trates como reglas absolutas si contradicen las restricciones actuales.");
        foreach (var decision in decisions)
        {
            var zone = string.IsNullOrWhiteSpace(decision.Zone) ? "zona no informada" : decision.Zone;
            var finalExercise = decision.FinalExerciseId.HasValue
                ? decision.FinalExerciseId.Value.ToString()
                : "sin ejercicio final";
            var percentage = decision.FinalPercentage.HasValue
                ? $"{decision.FinalPercentage.Value:0.##}%"
                : "sin porcentaje final";

            sb.AppendLine(
                $"- {decision.Date:yyyy-MM-dd} | origen: {decision.Source} | zona: {zone} | " +
                $"decisión: {decision.Decision} | sugerido: {decision.SuggestedExerciseId} | " +
                $"final: {finalExercise} | porcentaje final: {percentage}.");
        }
        sb.AppendLine("Priorizá los patrones repetidos del PF y explicá cuando una restricción actual obligue a apartarse de ellos.");
        sb.AppendLine();
    }

    /// <summary>
    /// Extrae los grupos musculares clave del texto del objetivo de entrenamiento (#158 · KAN-563 / KAN-605).
    /// Delega en <see cref="GoalMuscleFocusMapper"/> para mantenerse sincronizado con el motor de reglas.
    /// </summary>
    public static IReadOnlyList<string> ExtractMuscleGroups(string? goal) =>
        GoalMuscleFocusMapper.ExtractMuscleGroups(goal);

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

    // Intercala los ejercicios por grupo muscular manteniendo el orden del catálogo dentro de cada grupo
    private static List<CatalogExerciseDto> InterleaveByMuscleGroup(List<CatalogExerciseDto> exercises)
    {
        var buckets = exercises
            .GroupBy(e => TextNormalizer.Normalize(e.MuscleGroup))
            .Select(g => new Queue<CatalogExerciseDto>(g))
            .ToList();

        if (buckets.Count <= 1)
            return exercises;

        var result = new List<CatalogExerciseDto>(exercises.Count);
        while (result.Count < exercises.Count)
        {
            foreach (var bucket in buckets)
            {
                if (bucket.Count > 0)
                    result.Add(bucket.Dequeue());
            }
        }
        return result;
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
            .Select(TextNormalizer.Normalize)
            .ToHashSet();

        if (normalizedEmphasis.Count == 0)
        {
            return catalog.Take(maxExercises).ToList();
        }

        var matching = new List<CatalogExerciseDto>();
        var remaining = new List<CatalogExerciseDto>();

        foreach (var exercise in catalog)
        {
            var group = TextNormalizer.Normalize(exercise.MuscleGroup);
            var name = TextNormalizer.Normalize(exercise.Name);

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

        // Con varios grupos (ej. tren superior: pecho, espalda, hombros, brazos) se reparten por turnos, para que
        // el primer grupo del catálogo no llene todo el cupo y el modelo pueda armar una sesión equilibrada
        matching = InterleaveByMuscleGroup(matching);

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
