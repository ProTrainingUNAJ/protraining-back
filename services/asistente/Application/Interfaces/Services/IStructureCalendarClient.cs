namespace Application.Interfaces.Services;

public interface IStructureCalendarClient
{
    Task<IReadOnlyList<DateOnly>> GetMatchDatesAsync(
        Guid categoryId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
