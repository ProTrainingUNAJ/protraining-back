namespace Application.DTOs.TrainingSessions;

public record TrainingSessionDto(
    Guid Id,
    Guid TrainingWeekId,
    string Name,
    int Order,
    DayOfWeek DayOfWeek,
    string? CoachNotes
);
