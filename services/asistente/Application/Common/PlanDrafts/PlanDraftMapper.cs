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
}
