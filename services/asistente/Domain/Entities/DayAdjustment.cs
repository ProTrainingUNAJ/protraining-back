using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public enum DayAdjustmentStatus
{
    Pending,
    Approved,
    Rejected
}

/// <summary>
/// Propuesta de ajuste del día generada ante conflictos de dolor del check-in con la rutina de hoy (#157 · KAN-452).
/// El sustituto lo eligen las reglas (el primero de la matriz de restricciones) y la IA solo redacta la justificación.
/// </summary>
public sealed class DayAdjustment : BaseEntity
{
    public const int MaxJustificationLength = 1000;
    public const int MaxRejectionReasonLength = 500;

    public Guid AthleteId { get; private set; }
    public DateOnly Date { get; private set; }
    public Guid SessionId { get; private set; }
    public DayAdjustmentStatus Status { get; private set; } = DayAdjustmentStatus.Pending;
    public string Items { get; private set; } = "[]";
    public string Justification { get; private set; } = string.Empty;
    public string Provider { get; private set; } = "Rules";
    public string? RejectionReason { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private DayAdjustment() { }

    public static DayAdjustment Create(
        Guid athleteId,
        DateOnly date,
        Guid sessionId,
        string itemsJson,
        string justification,
        string provider)
    {
        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es obligatorio.");

        if (date == default)
            throw new DomainException("La fecha de la propuesta de ajuste es obligatoria.");

        if (sessionId == Guid.Empty)
            throw new DomainException("El identificador de la sesión de entrenamiento es obligatorio.");

        if (string.IsNullOrWhiteSpace(itemsJson))
            throw new DomainException("La propuesta de ajuste debe contener al menos un ejercicio en conflicto.");

        if (string.IsNullOrWhiteSpace(justification))
            throw new DomainException("La justificación del ajuste es obligatoria.");

        var trimmedJustification = justification.Trim();
        if (trimmedJustification.Length > MaxJustificationLength)
            trimmedJustification = trimmedJustification[..MaxJustificationLength];

        return new DayAdjustment
        {
            AthleteId = athleteId,
            Date = date,
            SessionId = sessionId,
            Status = DayAdjustmentStatus.Pending,
            Items = itemsJson.Trim(),
            Justification = trimmedJustification,
            Provider = string.IsNullOrWhiteSpace(provider) ? "Rules" : provider.Trim(),
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Approve(string updatedItemsJson, string? updatedJustification = null)
    {
        if (Status != DayAdjustmentStatus.Pending)
            throw new DomainException(GetNonPendingStatusMessage());

        if (string.IsNullOrWhiteSpace(updatedItemsJson))
            throw new DomainException("La propuesta aprobada debe contener al menos un cambio de ejercicio.");

        Items = updatedItemsJson.Trim();

        if (!string.IsNullOrWhiteSpace(updatedJustification))
        {
            var trimmed = updatedJustification.Trim();
            Justification = trimmed.Length > MaxJustificationLength
                ? trimmed[..MaxJustificationLength]
                : trimmed;
        }

        Status = DayAdjustmentStatus.Approved;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reject(string? reason = null)
    {
        if (Status != DayAdjustmentStatus.Pending)
            throw new DomainException(GetNonPendingStatusMessage());

        if (!string.IsNullOrWhiteSpace(reason))
        {
            var trimmed = reason.Trim();
            if (trimmed.Length > MaxRejectionReasonLength)
                throw new DomainException($"El motivo del rechazo no puede superar los {MaxRejectionReasonLength} caracteres.");

            RejectionReason = trimmed;
        }
        else
        {
            RejectionReason = null;
        }

        Status = DayAdjustmentStatus.Rejected;
        UpdatedAt = DateTime.UtcNow;
    }

    public string GetNonPendingStatusMessage() => Status switch
    {
        DayAdjustmentStatus.Approved => "La propuesta de ajuste del día ya fue aprobada.",
        DayAdjustmentStatus.Rejected => "La propuesta de ajuste del día ya fue rechazada.",
        _ => "La propuesta de ajuste del día se encuentra pendiente."
    };
}
