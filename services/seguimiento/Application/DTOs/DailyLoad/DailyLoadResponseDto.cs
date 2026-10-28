namespace Application.DTOs.DailyLoad;

public sealed record DailyLoadResponseDto(
    string Message,
    int TotalMinutes,
    DateOnly Date,
    ActivityLoadDto Gym,
    ActivityLoadDto SportTraining,
    ActivityLoadDto Match1,
    ActivityLoadDto Match2,
    ActivityLoadDto IndividualWork);
