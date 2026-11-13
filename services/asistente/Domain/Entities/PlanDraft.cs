using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public enum PlanDraftStatus
{
    Draft,
    Approved,
    Discarded
}

/// <summary>
/// Representa un borrador de planificación multisesión generado por el Asistente de IA (KAN-383 / KAN-410).
/// </summary>
public sealed class PlanDraft : BaseEntity
{
    public const int MaxGoalLength = 500;

    public Guid? CategoryId { get; private set; }
    public Guid? AthleteId { get; private set; }
    public string Goal { get; private set; } = string.Empty;
    public DateOnly StartDate { get; private set; }
    public PlanDraftStatus Status { get; private set; } = PlanDraftStatus.Draft;
    public string Content { get; private set; } = string.Empty;
    public string Provider { get; private set; } = string.Empty;
    public Guid? ApprovedPlanId { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private PlanDraft() { }

    public static PlanDraft Create(
        Guid? categoryId,
        Guid? athleteId,
        string goal,
        DateOnly startDate,
        string content,
        string provider)
    {
        var hasCategory = categoryId.HasValue && categoryId.Value != Guid.Empty;
        var hasAthlete = athleteId.HasValue && athleteId.Value != Guid.Empty;

        if (hasCategory == hasAthlete)
            throw new DomainException("Debés indicar una categoría o un atleta, pero no ambos a la vez.");

        if (string.IsNullOrWhiteSpace(goal))
            throw new DomainException("El objetivo de entrenamiento es obligatorio.");

        var trimmedGoal = goal.Trim();
        if (trimmedGoal.Length > MaxGoalLength)
            throw new DomainException($"El objetivo no puede superar los {MaxGoalLength} caracteres.");

        if (startDate == default)
            throw new DomainException("La fecha de inicio del borrador es obligatoria.");

        if (string.IsNullOrWhiteSpace(content))
            throw new DomainException("El contenido del borrador es obligatorio.");

        var now = DateTime.UtcNow;

        return new PlanDraft
        {
            CategoryId = hasCategory ? categoryId : null,
            AthleteId = hasAthlete ? athleteId : null,
            Goal = trimmedGoal,
            StartDate = startDate,
            Status = PlanDraftStatus.Draft,
            Content = content.Trim(),
            Provider = string.IsNullOrWhiteSpace(provider) ? "Rules" : provider.Trim(),
            UpdatedAt = now
        };
    }

    public void UpdateContent(string newContent)
    {
        if (Status != PlanDraftStatus.Draft)
            throw new DomainException(GetNonEditableStatusMessage());

        if (string.IsNullOrWhiteSpace(newContent))
            throw new DomainException("El contenido del borrador es obligatorio.");

        Content = newContent.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void Discard()
    {
        if (Status != PlanDraftStatus.Draft)
            throw new DomainException(GetNonEditableStatusMessage());

        Status = PlanDraftStatus.Discarded;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Approve(Guid approvedPlanId)
    {
        if (Status == PlanDraftStatus.Discarded)
            throw new DomainException("El borrador ya fue descartado.");

        if (approvedPlanId == Guid.Empty)
            throw new DomainException("El identificador de la planificación aprobada es obligatorio.");

        ApprovedPlanId = approvedPlanId;
        Status = PlanDraftStatus.Approved;
        UpdatedAt = DateTime.UtcNow;
    }

    public string GetNonEditableStatusMessage() => Status switch
    {
        PlanDraftStatus.Approved => "El borrador ya fue aprobado.",
        PlanDraftStatus.Discarded => "El borrador ya fue descartado.",
        _ => "El borrador se encuentra en estado editable."
    };
}
