using Application.DTOs.CheckIns;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.CheckIns.ListAthleteCheckIns;

public sealed class ListAthleteCheckInsQueryHandler(
    ICheckInRepository repository,
    ICheckInInjuryPainRepository injuryPainRepository)
{
    public async Task<IReadOnlyList<CheckInDto>> HandleAsync(
        ListAthleteCheckInsQuery query,
        CancellationToken cancellationToken = default)
    {
        var startDate = query.From?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endDate = query.To?.ToDateTime(new TimeOnly(23, 59, 59, 999), DateTimeKind.Utc);

        var limit = query.Limit ?? (!query.From.HasValue && !query.To.HasValue ? 14 : (int?)null);

        var checkIns = await repository.GetByAthleteAndDateRangeAsync(
            query.AthleteId,
            startDate,
            endDate,
            limit,
            cancellationToken);

        if (checkIns.Count == 0)
            return Array.Empty<CheckInDto>();

        var painsByCheckIn = await injuryPainRepository.ListByCheckInIdsAsync(
            checkIns.Select(c => c.Id).ToList(),
            cancellationToken);

        return checkIns
            .Select(checkIn => CheckInMappings.ToDto(
                checkIn,
                painsByCheckIn.TryGetValue(checkIn.Id, out var pains)
                    ? pains
                    : Array.Empty<CheckInInjuryPainDto>()))
            .ToList();
    }
}
