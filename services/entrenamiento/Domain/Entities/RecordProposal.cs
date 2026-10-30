using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public class RecordProposal : BaseEntity
{
    public Guid AthleteId { get; private set; }
    public Guid ExerciseId { get; private set; }
    public decimal CurrentWeight { get; private set; }
    public decimal ProposedWeight { get; private set; }
    public decimal PerformedWeight { get; private set; }
    public int PerformedReps { get; private set; }
    public RecordProposalStatus Status { get; private set; }

    public Exercise? Exercise { get; private set; }

    private RecordProposal() { }

    public static RecordProposal Create(
        Guid athleteId,
        Guid exerciseId,
        decimal currentWeight,
        decimal proposedWeight,
        decimal performedWeight,
        int performedReps)
    {
        if (athleteId == Guid.Empty || exerciseId == Guid.Empty)
            throw new DomainException("El atleta y el ejercicio son obligatorios.");

        if (currentWeight <= 0)
            throw new DomainException("El peso del récord vigente debe ser mayor a 0 kg.");

        if (proposedWeight <= currentWeight)
            throw new DomainException("El peso propuesto debe ser mayor al récord vigente.");

        if (performedWeight <= 0)
            throw new DomainException("El peso realizado debe ser mayor a 0 kg.");

        if (performedReps <= 0)
            throw new DomainException("Las repeticiones realizadas deben ser mayores a 0.");

        return new RecordProposal
        {
            AthleteId = athleteId,
            ExerciseId = exerciseId,
            CurrentWeight = currentWeight,
            ProposedWeight = proposedWeight,
            PerformedWeight = performedWeight,
            PerformedReps = performedReps,
            Status = RecordProposalStatus.Pending
        };
    }

    public void UpdateProposal(decimal proposedWeight, decimal performedWeight, int performedReps)
    {
        if (proposedWeight <= CurrentWeight)
            throw new DomainException("El nuevo peso propuesto debe ser mayor al récord vigente.");

        ProposedWeight = proposedWeight;
        PerformedWeight = performedWeight;
        PerformedReps = performedReps;
        Status = RecordProposalStatus.Pending;
    }

    public void Approve()
    {
        Status = RecordProposalStatus.Approved;
    }

    public void Discard()
    {
        Status = RecordProposalStatus.Discarded;
    }
}

public enum RecordProposalStatus
{
    Pending,
    Approved,
    Discarded
}

