namespace Application.Common.PlanDrafts;

using System.Text.Json;
using Application.Common.Ai;
using Application.Common.Helpers;
using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;

/// <summary>
/// Parser y traductor del borrador de sesiones modelo por bloques con selección numérica.
/// Convierte los índices numéricos (1..N) de los bloques a GUIDs reales del catálogo
/// y aplica la progresión determinística en C# para proyectar las semanas 2 a N.
/// </summary>
public static class PlanDraftBlockParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parsea la respuesta estructurada por bloques de la IA y genera el borrador de planificación multisesión completo.
    /// </summary>
    public static (bool IsValid, PlanDraftContentDto? Content, string? ErrorMessage) ParseAndExpand(
        string rawResponse,
        IReadOnlyList<CatalogExerciseDto> catalog,
        int requestedWeeks,
        DateOnly? startDate = null,
        IReadOnlyList<DateOnly>? matchDates = null)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return (false, null, "El texto recibido de la IA está vacío.");
        }

        if (catalog == null || catalog.Count == 0)
        {
            return (false, null, "El catálogo de ejercicios está vacío. No se pueden traducir los índices de bloques.");
        }

        var cleanedJson = AiRoutineParser.CleanMarkdownFences(rawResponse);

        try
        {
            // 1. Intentar deserializar como modelo estructurado por bloques funcionales
            var blockModel = JsonSerializer.Deserialize<BlockWeekModelDto>(cleanedJson, JsonOptions);
            if (blockModel?.ModelSessions is { Count: > 0 })
            {
                var modelSessions = new List<PlanDraftSessionDto>();

                for (var sessionIdx = 0; sessionIdx < blockModel.ModelSessions.Count; sessionIdx++)
                {
                    var blockSession = blockModel.ModelSessions[sessionIdx];
                    var dayOfWeek = ParseDayOfWeek(blockSession.DayOfWeek);
                    var sessionExercises = new List<PlanDraftExerciseDto>();
                    var currentOrder = 1;

                    // Aplanar los 4 bloques en la secuencia funcional oficial
                    if (AppendBlock(sessionExercises, blockSession.WarmUp, catalog, ref currentOrder, defaultSets: 2, defaultReps: 10, defaultPct: null) is { } errWarmUp)
                        return (false, null, errWarmUp);
                    if (AppendBlock(sessionExercises, blockSession.StrengthA, catalog, ref currentOrder, defaultSets: 4, defaultReps: 6, defaultPct: 80m) is { } errStrA)
                        return (false, null, errStrA);
                    if (AppendBlock(sessionExercises, blockSession.StrengthB, catalog, ref currentOrder, defaultSets: 3, defaultReps: 8, defaultPct: 75m) is { } errStrB)
                        return (false, null, errStrB);
                    if (AppendBlock(sessionExercises, blockSession.Accessories, catalog, ref currentOrder, defaultSets: 3, defaultReps: 12, defaultPct: null) is { } errAcc)
                        return (false, null, errAcc);

                    if (sessionExercises.Count == 0)
                    {
                        return (false, null, $"La sesión '{blockSession.Name}' no contiene ejercicios válidos en sus bloques.");
                    }

                    modelSessions.Add(new PlanDraftSessionDto(
                        Name: string.IsNullOrWhiteSpace(blockSession.Name) ? $"Sesión {sessionIdx + 1}" : blockSession.Name.Trim(),
                        DayOfWeek: dayOfWeek,
                        Order: blockSession.Order > 0 ? blockSession.Order : sessionIdx + 1,
                        CoachNotes: blockSession.CoachNotes,
                        Exercises: sessionExercises
                    ));
                }

                // 2. Proyectar las semanas 2 a N con el motor de progresión en C#
                var progressionWeeks = WeeklyProgressionEngine.GenerateProgressionWeeks(
                    modelSessions,
                    requestedWeeks,
                    startDate,
                    matchDates);

                var content = new PlanDraftContentDto(
                    Name: string.IsNullOrWhiteSpace(blockModel.Name) ? "Planificación Deportiva por Bloques" : blockModel.Name.Trim(),
                    Objective: string.IsNullOrWhiteSpace(blockModel.Objective) ? "Desarrollo atlético integral" : blockModel.Objective.Trim(),
                    JustificationSummary: string.IsNullOrWhiteSpace(blockModel.JustificationSummary)
                        ? "Borrador armado por bloques funcionales con progresión determinística semanal."
                        : blockModel.JustificationSummary.Trim(),
                    Weeks: progressionWeeks
                );

                return (true, content, null);
            }

            // 3. Fallback retrocompatible: si vino en el formato clásico PlanDraftContentDto directo
            var fallbackContent = PlanDraftMapper.DeserializeContent(cleanedJson);
            if (fallbackContent?.Weeks is { Count: > 0 })
            {
                // Si solo vino 1 semana y pidieron más, la expandimos con el motor de progresión
                if (fallbackContent.Weeks.Count == 1 && requestedWeeks > 1 && fallbackContent.Weeks[0].Sessions is { Count: > 0 })
                {
                    var expandedWeeks = WeeklyProgressionEngine.GenerateProgressionWeeks(
                        fallbackContent.Weeks[0].Sessions,
                        requestedWeeks,
                        startDate,
                        matchDates);

                    fallbackContent = fallbackContent with { Weeks = expandedWeeks };
                }

                return (true, fallbackContent, null);
            }

            return (false, null, "La respuesta no contiene sesiones modelo por bloques ni semanas válidas.");
        }
        catch (JsonException ex)
        {
            return (false, null, $"Error de sintaxis JSON en la estructura por bloques: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, null, $"Error al interpretar el borrador por bloques: {ex.Message}");
        }
    }

    private static string? AppendBlock(
        List<PlanDraftExerciseDto> targetList,
        List<BlockExerciseItemDto>? blockItems,
        IReadOnlyList<CatalogExerciseDto> catalog,
        ref int currentOrder,
        int defaultSets,
        int defaultReps,
        decimal? defaultPct)
    {
        if (blockItems == null || blockItems.Count == 0)
            return null;

        foreach (var item in blockItems)
        {
            var sets = item.Sets > 0 ? item.Sets : defaultSets;
            var reps = item.Reps > 0 ? item.Reps : defaultReps;
            var pct = item.TargetPercentage ?? defaultPct;

            // Si viene un índice válido (1..N), resolvemos directo del catálogo
            var byIndex = item.ExerciseIndex is > 0
                ? ResolveCatalogExercise(item.ExerciseIndex.Value, catalog)
                : null;
            if (byIndex != null)
            {
                targetList.Add(new PlanDraftExerciseDto(
                    ExerciseId: byIndex.Id,
                    Order: currentOrder++,
                    Sets: sets,
                    Reps: reps,
                    TargetPercentage: pct,
                    ExerciseName: byIndex.Name,
                    IsReference: false,
                    MuscleGroup: byIndex.MuscleGroup
                ));
                continue;
            }

            // Si no vino un índice válido, buscamos por nombre (por si ya existe en el catálogo)
            if (!string.IsNullOrWhiteSpace(item.ExerciseName))
            {
                var normalizedTarget = TextNormalizer.Normalize(item.ExerciseName);
                var byName = catalog.FirstOrDefault(c =>
                    TextNormalizer.Normalize(c.Name) == normalizedTarget);
                if (byName != null)
                {
                    targetList.Add(new PlanDraftExerciseDto(
                        ExerciseId: byName.Id,
                        Order: currentOrder++,
                        Sets: sets,
                        Reps: reps,
                        TargetPercentage: pct,
                        ExerciseName: byName.Name,
                        IsReference: false,
                        MuscleGroup: byName.MuscleGroup
                    ));
                    continue;
                }

                // Si no existe en el catálogo -> se trae como ejercicio de referencia
                targetList.Add(new PlanDraftExerciseDto(
                    ExerciseId: null,
                    Order: currentOrder++,
                    Sets: sets,
                    Reps: reps,
                    TargetPercentage: pct,
                    ExerciseName: item.ExerciseName.Trim(),
                    IsReference: true,
                    MuscleGroup: string.IsNullOrWhiteSpace(item.MuscleGroup) ? null : item.MuscleGroup.Trim()
                ));
                continue;
            }

            // Fallback si vino índice 0 sin exerciseName (retrocompatibilidad)
            var zeroFallback = item.ExerciseIndex.HasValue
                ? ResolveCatalogExercise(item.ExerciseIndex.Value, catalog)
                : null;
            if (zeroFallback == null)
            {
                return $"El ejercicio con índice {item.ExerciseIndex} no existe en el catálogo disponible (1..{catalog.Count}) y no trajo 'exerciseName'.";
            }
            targetList.Add(new PlanDraftExerciseDto(
                ExerciseId: zeroFallback.Id,
                Order: currentOrder++,
                Sets: sets,
                Reps: reps,
                TargetPercentage: pct,
                ExerciseName: zeroFallback.Name,
                IsReference: false,
                MuscleGroup: zeroFallback.MuscleGroup
            ));
        }

        return null;
    }

    private static CatalogExerciseDto? ResolveCatalogExercise(int index, IReadOnlyList<CatalogExerciseDto> catalog)
    {
        // El modelo devuelve índice basado en 1 (1..catalog.Count)
        if (index >= 1 && index <= catalog.Count)
        {
            return catalog[index - 1];
        }

        // Si devuelve índice basado en 0 por error
        if (index >= 0 && index < catalog.Count)
        {
            return catalog[index];
        }

        return null;
    }

    private static DayOfWeek ParseDayOfWeek(string? rawDay)
    {
        if (string.IsNullOrWhiteSpace(rawDay))
            return DayOfWeek.Monday;

        var clean = TextNormalizer.Normalize(rawDay);

        if (clean.StartsWith("lun") || clean.StartsWith("mon")) return DayOfWeek.Monday;
        if (clean.StartsWith("mar") || clean.StartsWith("tue")) return DayOfWeek.Tuesday;
        if (clean.StartsWith("mie") || clean.StartsWith("wed")) return DayOfWeek.Wednesday;
        if (clean.StartsWith("jue") || clean.StartsWith("thu")) return DayOfWeek.Thursday;
        if (clean.StartsWith("vie") || clean.StartsWith("fri")) return DayOfWeek.Friday;
        if (clean.StartsWith("sab") || clean.StartsWith("sat")) return DayOfWeek.Saturday;
        if (clean.StartsWith("dom") || clean.StartsWith("sun")) return DayOfWeek.Sunday;

        if (Enum.TryParse<DayOfWeek>(rawDay, ignoreCase: true, out var parsed))
            return parsed;

        return DayOfWeek.Monday;
    }
}
