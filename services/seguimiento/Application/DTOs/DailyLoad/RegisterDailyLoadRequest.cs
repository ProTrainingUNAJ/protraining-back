namespace Application.DTOs.DailyLoad;

public sealed record RegisterDailyLoadRequest
{
    public ActivityLoadDto? Gym { get; init; }
    public ActivityLoadDto? SportTraining { get; init; }
    public ActivityLoadDto? Match1 { get; init; }
    public ActivityLoadDto? Match2 { get; init; }
    public ActivityLoadDto? IndividualWork { get; init; }
}
