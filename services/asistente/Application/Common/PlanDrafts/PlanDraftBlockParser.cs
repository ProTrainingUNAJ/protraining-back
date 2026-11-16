namespace Application.Common.PlanDrafts;

using System.Text.Json;
using Application.Common.Ai;
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
                    var dayOfWeek = FlexibleDayOfWeekConverter.ParseDayOfWeek(blockSession.DayOfWeek) ?? DayOfWeek.Monday;
                    var dayOfWeek = ParseDayOfWeekFromString(blockSession.DayOfWeek);
                    var sessionExercises = new List<PlanDraftExerciseDto>();
                    var currentOrder = 1;

                    // Aplanar los 4 bloques en la secuencia funcional oficial
                    AppendBlock(sessionExercises, blockSession.WarmUp, catalog, ref currentOrder, defaultSets: 2, defaultReps: 10, defaultPct: null);
                    AppendBlock(sessionExercises, blockSession.StrengthA, catalog, ref currentOrder, defaultSets: 4, defaultReps: 6, defaultPct: 80m);
                    AppendBlock(sessionExercises, blockSession.StrengthB, catalog, ref currentOrder, defaultSets: 3, defaultReps: 8, defaultPct: 75m);
                    AppendBlock(sessionExercises, blockSession.Accessories, catalog, ref currentOrder, defaultSets: 3, defaultReps: 12, defaultPct: null);

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

    private static void AppendBlock(
        List<PlanDraftExerciseDto> targetList,
        List<BlockExerciseItemDto>? blockItems,
        IReadOnlyList<CatalogExerciseDto> catalog,
        ref int currentOrder,
        int defaultSets,
        int defaultReps,
        decimal? defaultPct)
    {
        if (blockItems == null || blockItems.Count == 0)
            return;

        foreach (var item in blockItems)
        {
            // Traducir número en lista (1..N) a ejercicio real del catálogo
            var exercise = ResolveCatalogExercise(item.ExerciseIndex, catalog);
            if (exercise == null)
                continue;

            targetList.Add(new PlanDraftExerciseDto(
                ExerciseId: exercise.Id,
                Order: currentOrder++,
                Sets: item.Sets > 0 ? item.Sets : defaultSets,
                Reps: item.Reps > 0 ? item.Reps : defaultReps,
                TargetPercentage: item.TargetPercentage ?? defaultPct
            ));
        }
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
}
