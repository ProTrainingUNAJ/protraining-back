namespace Application.DTOs.TrainingSessions;

public sealed record ScheduledSessionTargetsResponse(
    IReadOnlyList<Guid>? AthleteIds = null,
    IReadOnlyList<Guid>? CategoryIds = null)
{
    public IReadOnlyList<Guid> AthleteIds { get; init; } = AthleteIds ?? Array.Empty<Guid>();
    public IReadOnlyList<Guid> CategoryIds { get; init; } = CategoryIds ?? Array.Empty<Guid>();
}
