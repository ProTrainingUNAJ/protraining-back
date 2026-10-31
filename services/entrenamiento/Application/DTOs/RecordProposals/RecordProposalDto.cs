namespace Application.DTOs.RecordProposals;

public record RecordProposalDto(
    Guid Id,
    Guid AthleteId,
    string AthleteName,
    Guid ExerciseId,
    string ExerciseName,
    decimal CurrentWeight,
    decimal ProposedWeight,
    decimal PerformedWeight,
    int PerformedReps,
    string Status,
    DateTime CreatedAt
);

