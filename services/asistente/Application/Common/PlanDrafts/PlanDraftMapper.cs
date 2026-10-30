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
            Weeks: content.Weeks,
            Warnings: warnings ?? []
        );
    }

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
                                ExerciseId: e.ExerciseId,
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
