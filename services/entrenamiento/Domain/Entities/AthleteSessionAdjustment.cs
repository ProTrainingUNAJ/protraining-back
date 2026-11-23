using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public static class AthleteSessionAdjustmentStatuses
{
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

/// <summary>
/// Ajuste o decisión registrada por el preparador físico para la sesión de hoy de un atleta ante dolor o lesión (#157 · KAN-452, #168 · KAN-463, KAN-636).
/// </summary>
public sealed class AthleteSessionAdjustment : BaseEntity
{
    public const int MaxNoteLength = 1000;

    public Guid TrainingSessionId { get; private set; }
    public Guid AthleteId { get; private set; }
    public Guid TrainingPlanId { get; private set; }
    public DateOnly Date { get; private set; }
    public string? Note { get; private set; }
    public string? RejectionReason { get; private set; }
    public string Items { get; private set; } = "[]";
    public int? LoadReductionPercentage { get; private set; }
    public string Status { get; private set; } = AthleteSessionAdjustmentStatuses.Approved;
    public DateTime UpdatedAt { get; private set; }

    public bool IsRejected =>
        string.Equals(Status, AthleteSessionAdjustmentStatuses.Rejected, StringComparison.OrdinalIgnoreCase);

    private AthleteSessionAdjustment() { }

    private static string? NormalizeNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return null;

        var trimmed = note.Trim();
        if (trimmed.Length > MaxNoteLength)
            throw new DomainException($"La nota no puede superar los {MaxNoteLength} caracteres.");

        return trimmed;
    }

    public static AthleteSessionAdjustment Create(
        Guid trainingSessionId,
        Guid athleteId,
        DateOnly date,
        string itemsJson,
        string? note = null,
        Guid? trainingPlanId = null,
        int? loadReductionPercentage = null)
    {
        if (trainingSessionId == Guid.Empty)
            throw new DomainException("El identificador de la sesión de entrenamiento es obligatorio.");

        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es obligatorio.");

        if (date == default)
            throw new DomainException("La fecha del ajuste es obligatoria.");

        var normalizedItems = string.IsNullOrWhiteSpace(itemsJson) ? "[]" : itemsJson.Trim();
        var normalizedNote = NormalizeNote(note);

        if (loadReductionPercentage.HasValue && (loadReductionPercentage.Value <= 0 || loadReductionPercentage.Value > 100))
            throw new DomainException("El porcentaje de reducción de carga debe estar entre 1 y 100.");

        if (normalizedItems == "[]" && !loadReductionPercentage.HasValue && normalizedNote is null)
            throw new DomainException("Debe incluir al menos un cambio de ejercicio o una reducción de carga para la sesión.");

        return new AthleteSessionAdjustment
        {
            TrainingSessionId = trainingSessionId,
            AthleteId = athleteId,
            TrainingPlanId = trainingPlanId ?? Guid.Empty,
            Date = date,
            Items = normalizedItems,
            Note = normalizedNote,
            RejectionReason = null,
            LoadReductionPercentage = loadReductionPercentage,
            Status = AthleteSessionAdjustmentStatuses.Approved,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static AthleteSessionAdjustment CreateRejected(
        Guid trainingSessionId,
        Guid athleteId,
        DateOnly date,
        string? rejectionReason = null,
        Guid? trainingPlanId = null)
    {
        if (trainingSessionId == Guid.Empty)
            throw new DomainException("El identificador de la sesión de entrenamiento es obligatorio.");

        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es obligatorio.");

        if (date == default)
            throw new DomainException("La fecha del rechazo es obligatoria.");

        var normalizedReason = NormalizeNote(rejectionReason);

        return new AthleteSessionAdjustment
        {
            TrainingSessionId = trainingSessionId,
            AthleteId = athleteId,
            TrainingPlanId = trainingPlanId ?? Guid.Empty,
            Date = date,
            Items = "[]",
            Note = null,
            RejectionReason = normalizedReason,
            LoadReductionPercentage = null,
            Status = AthleteSessionAdjustmentStatuses.Rejected,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Update(string itemsJson, string? note = null, Guid? trainingPlanId = null, int? loadReductionPercentage = null)
    {
        var normalizedItems = string.IsNullOrWhiteSpace(itemsJson) ? "[]" : itemsJson.Trim();
        var normalizedNote = NormalizeNote(note);

        if (loadReductionPercentage.HasValue && (loadReductionPercentage.Value <= 0 || loadReductionPercentage.Value > 100))
            throw new DomainException("El porcentaje de reducción de carga debe estar entre 1 y 100.");

        if (normalizedItems == "[]" && !loadReductionPercentage.HasValue && normalizedNote is null)
            throw new DomainException("Debe incluir al menos un cambio de ejercicio o una reducción de carga para la sesión.");

        Items = normalizedItems;
        Note = normalizedNote;
        RejectionReason = null;
        if (trainingPlanId.HasValue && trainingPlanId.Value != Guid.Empty)
        {
            TrainingPlanId = trainingPlanId.Value;
        }
        LoadReductionPercentage = loadReductionPercentage;
        Status = AthleteSessionAdjustmentStatuses.Approved;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkRejected(string? rejectionReason = null, Guid? trainingPlanId = null)
    {
        var normalizedReason = NormalizeNote(rejectionReason);

        // Se conservan Items, Note y LoadReductionPercentage previos para mantener trazabilidad de lo que se había aprobado.
        RejectionReason = normalizedReason;
        if (trainingPlanId.HasValue && trainingPlanId.Value != Guid.Empty)
        {
            TrainingPlanId = trainingPlanId.Value;
        }
        Status = AthleteSessionAdjustmentStatuses.Rejected;
        UpdatedAt = DateTime.UtcNow;
    }
}
