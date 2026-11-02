using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public enum AlertSeverity
{
    Low,
    Medium,
    High
}

public enum AlertStatus
{
    Pending,
    Reviewed,
    Applied,
    Dismissed
}

public enum AlertType
{
    AccumulatedFatigue,
    AcuteOverload,
    InjuryRisk,
    WellnessAlert
}

public enum SuggestedAdjustmentType
{
    LoadReduction,
    RoutineAdjustment,
    Rest
}

/// <summary>
/// Representa un aviso preventivo generado para el preparador físico (PF) ante indicadores
/// de fatiga acumulada, sobrecarga o riesgo de lesión (#179 · KAN-474).
/// Almacena una propuesta concreta de ajuste de carga para la toma de decisión del PF.
/// </summary>
public sealed class PreventiveAlert : BaseEntity
{
    public const int MaxAthleteNameLength = 200;
    public const int MaxReasonLength = 1000;
    public const int MaxSuggestionLength = 1000;

    public Guid AthleteId { get; private set; }
    public string AthleteName { get; private set; } = string.Empty;
    public Guid? CategoryId { get; private set; }
    public AlertSeverity Severity { get; private set; } = AlertSeverity.Medium;
    public AlertType Type { get; private set; } = AlertType.AccumulatedFatigue;
    public string Reason { get; private set; } = string.Empty;
    public string Suggestion { get; private set; } = string.Empty;
    public SuggestedAdjustmentType? SuggestedAdjustmentType { get; private set; }
    public int? SuggestedAdjustmentPercentage { get; private set; }
    public string? SuggestedAdjustmentPayload { get; private set; }
    public AlertStatus Status { get; private set; } = AlertStatus.Pending;
    public DateTime GeneratedAtUtc { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public DateTime? AppliedAtUtc { get; private set; }

    public bool Reviewed => Status != AlertStatus.Pending;

    private PreventiveAlert() { }

    public static PreventiveAlert Create(
        Guid athleteId,
        string athleteName,
        string reason,
        string suggestion,
        AlertSeverity severity = AlertSeverity.Medium,
        AlertType type = AlertType.AccumulatedFatigue,
        SuggestedAdjustmentType? suggestedAdjustmentType = null,
        int? suggestedAdjustmentPercentage = null,
        string? suggestedAdjustmentPayload = null,
        Guid? categoryId = null,
        DateTime? generatedAtUtc = null)
    {
        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es obligatorio.");

        if (string.IsNullOrWhiteSpace(athleteName))
            throw new DomainException("El nombre del atleta es obligatorio.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("El motivo o causa de la alerta es obligatorio.");

        if (string.IsNullOrWhiteSpace(suggestion))
            throw new DomainException("La sugerencia concreta de ajuste es obligatoria.");

        if (suggestedAdjustmentPercentage.HasValue && (suggestedAdjustmentPercentage.Value <= 0 || suggestedAdjustmentPercentage.Value > 100))
            throw new DomainException("El porcentaje de ajuste debe estar entre 1 y 100.");

        return new PreventiveAlert
        {
            AthleteId = athleteId,
            AthleteName = athleteName.Trim(),
            CategoryId = categoryId,
            Severity = severity,
            Type = type,
            Reason = reason.Trim(),
            Suggestion = suggestion.Trim(),
            SuggestedAdjustmentType = suggestedAdjustmentType,
            SuggestedAdjustmentPercentage = suggestedAdjustmentPercentage,
            SuggestedAdjustmentPayload = string.IsNullOrWhiteSpace(suggestedAdjustmentPayload) ? null : suggestedAdjustmentPayload.Trim(),
            Status = AlertStatus.Pending,
            GeneratedAtUtc = generatedAtUtc ?? DateTime.UtcNow
        };
    }

    public void MarkAsReviewed(DateTime? utcNow = null)
    {
        if (Status == AlertStatus.Pending)
        {
            Status = AlertStatus.Reviewed;
            ReviewedAtUtc = utcNow ?? DateTime.UtcNow;
        }
    }

    public void MarkAsApplied(DateTime? utcNow = null)
    {
        Status = AlertStatus.Applied;
        AppliedAtUtc = utcNow ?? DateTime.UtcNow;
        if (!ReviewedAtUtc.HasValue)
        {
            ReviewedAtUtc = AppliedAtUtc;
        }
    }

    public void Dismiss(DateTime? utcNow = null)
    {
        Status = AlertStatus.Dismissed;
        ReviewedAtUtc ??= utcNow ?? DateTime.UtcNow;
    }
}

