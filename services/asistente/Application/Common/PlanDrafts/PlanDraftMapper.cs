using System.Text.Json;
using System.Text.Json.Serialization;
using Application.DTOs.PlanDrafts;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.Common.PlanDrafts;

public static class PlanDraftMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string SerializeContent(PlanDraftContentDto content) =>
        JsonSerializer.Serialize(content, JsonOptions);

    public static PlanDraftContentDto DeserializeContent(string contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
            throw new DomainException("El contenido almacenado del borrador está vacío.");

        var parsed = JsonSerializer.Deserialize<PlanDraftContentDto>(contentJson, JsonOptions);
        return parsed ?? throw new DomainException("No se pudo interpretar el contenido JSON del borrador.");
    }

    public static PlanDraftResponseDto ToResponseDto(
        PlanDraft draft,
        PlanDraftContentDto? parsedContent = null,
        IReadOnlyList<string>? warnings = null)
    {
        var content = parsedContent ?? DeserializeContent(draft.Content);

        // Asegurar que cualquier ejercicio sin ID oficial se marque como referencia (KAN-467)
        var normalizedWeeks = (content.Weeks ?? []).Select(w => new PlanDraftWeekDto(
            w.WeekNumber,
            (w.Sessions ?? []).Select(s => new PlanDraftSessionDto(
                s.Name,
                s.DayOfWeek,
                s.Order,
                s.CoachNotes,
                (s.Exercises ?? []).Select(e =>
                    (!e.ExerciseId.HasValue || e.ExerciseId.Value == Guid.Empty)
                        ? e with { IsReference = true }
                        : e
                ).ToList()
            )).ToList()
        )).ToList();

        return new PlanDraftResponseDto(
            Id: draft.Id,
            CategoryId: draft.CategoryId,
            AthleteId: draft.AthleteId,
            Goal: draft.Goal,
            StartDate: draft.StartDate,
            Status: draft.Status.ToString(),
            Provider: draft.Provider,
            ApprovedPlanId: draft.ApprovedPlanId,
            Name: content.Name,
            Objective: content.Objective,
            JustificationSummary: content.JustificationSummary,
            Weeks: normalizedWeeks,
            Warnings: [.. ProviderWarnings(draft.Provider), .. warnings ?? []]
        );
    }

    // El PF tiene que saber qué proveedor armó el borrador. Se arma a partir del proveedor
    // guardado, así el aviso aparece también al volver a abrir el borrador (los demás avisos no se guardan).
    public static IReadOnlyList<string> ProviderWarnings(string? provider) => provider switch
    {
        "Gemini" or "Agente (Gemini)" =>
        [
            "El borrador lo armó el respaldo externo (Gemini), sin nombres ni datos personales de los atletas."
        ],
        "Reglas" =>
        [
            "Los proveedores de IA no devolvieron un borrador válido a tiempo, así que lo armaron las reglas fijas de ProTraining. Revisalo y ajustalo antes de aprobarlo."
        ],
        _ => []
    };

    public static SaveTrainingPlanRequestDto ToSaveTrainingPlanRequest(
        PlanDraft draft,
        PlanDraftContentDto? parsedContent = null)
    {
        var content = parsedContent ?? DeserializeContent(draft.Content);
        return ToSaveTrainingPlanRequest(content, draft.Goal);
    }

    public static SaveTrainingPlanRequestDto ToSaveTrainingPlanRequest(
        PlanDraftContentDto content,
        string? fallbackObjective = null)
    {
        var objective = !string.IsNullOrWhiteSpace(content.Objective)
            ? content.Objective.Trim()
            : fallbackObjective?.Trim();

        var weeks = (content.Weeks ?? [])
            .Select(w => new SaveTrainingPlanWeekRequestDto(
                WeekNumber: w.WeekNumber,
                Sessions: (w.Sessions ?? [])
                    .Select(s => new SaveTrainingPlanSessionRequestDto(
                        Name: s.Name,
                        Order: s.Order,
                        DayOfWeek: s.DayOfWeek,
                        CoachNotes: s.CoachNotes,
                        Exercises: (s.Exercises ?? [])
                            .Select(e => new SaveTrainingPlanExerciseRequestDto(
                                ExerciseId: e.ExerciseId is { } exerciseId && exerciseId != Guid.Empty
                                    ? exerciseId
                                    : throw new DomainException($"El ejercicio '{e.ExerciseName ?? "sin nombre"}' todavía no tiene un exerciseId válido para guardar el plan."),
                                Order: e.Order,
                                Sets: e.Sets,
                                Reps: e.Reps,
                                TargetPercentage: e.TargetPercentage))
                            .ToList()))
                    .ToList()))
            .ToList();

        return new SaveTrainingPlanRequestDto(
            Name: content.Name.Trim(),
            Objective: objective,
            Weeks: weeks
        );
    }

    public static AssignTrainingPlanRequestDto ToAssignTrainingPlanRequest(
        PlanDraft draft,
        Guid trainingPlanId) =>
        new(
            TrainingPlanId: trainingPlanId,
            StartDate: draft.StartDate,
            CategoryId: draft.CategoryId,
            AthleteId: draft.AthleteId
        );
}
