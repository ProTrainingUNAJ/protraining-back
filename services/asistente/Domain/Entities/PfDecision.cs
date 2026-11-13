using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public enum PfDecisionSource
{
    DayAdjustment,
    PlanDraft
}

public enum PfDecisionType
{
    Approved,
    Modified,
    Rejected
}

/// <summary>
/// Registra las decisiones del preparador físico sobre propuestas de la IA (memoria del PF, KAN-423 / KAN-410).
/// </summary>
public sealed class PfDecision : BaseEntity
{
    public Guid? AthleteId { get; private set; }
    public DateOnly Date { get; private set; }
    public PfDecisionSource Source { get; private set; }
    public Guid SourceId { get; private set; }
    public string? Zone { get; private set; }
    public Guid SuggestedExerciseId { get; private set; }
    public PfDecisionType Decision { get; private set; }
    public Guid? FinalExerciseId { get; private set; }
    public decimal? FinalPercentage { get; private set; }
    public string? Reason { get; private set; }

    private PfDecision() { }

    public static PfDecision Create(
        Guid? athleteId,
        PfDecisionSource source,
        Guid sourceId,
        Guid suggestedExerciseId,
        PfDecisionType decision,
        Guid? finalExerciseId = null,
        decimal? finalPercentage = null,
        string? zone = null,
        string? reason = null,
        DateOnly? date = null)
    {
        if (sourceId == Guid.Empty)
            throw new DomainException("El identificador de origen de la decisión es obligatorio.");

        if (suggestedExerciseId == Guid.Empty)
            throw new DomainException("El identificador del ejercicio sugerido es obligatorio.");

        if (decision == PfDecisionType.Modified && (!finalExerciseId.HasValue || finalExerciseId.Value == Guid.Empty))
            throw new DomainException("Una decisión modificada requiere indicar el ejercicio final elegido.");

        return new PfDecision
        {
            AthleteId = athleteId.HasValue && athleteId.Value != Guid.Empty ? athleteId : null,
            Date = date ?? ArgentinaTime.Today,
            Source = source,
            SourceId = sourceId,
            Zone = string.IsNullOrWhiteSpace(zone) ? null : zone.Trim(),
            SuggestedExerciseId = suggestedExerciseId,
            Decision = decision,
            FinalExerciseId = finalExerciseId.HasValue && finalExerciseId.Value != Guid.Empty ? finalExerciseId : null,
            FinalPercentage = finalPercentage,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
        };
    }
}
