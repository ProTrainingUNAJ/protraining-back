namespace Application.UseCases.TrainingSessions.UpdateCoachNotes;

public record UpdateCoachNotesCommand(
    Guid SessionId,
    string? CoachNotes
);
