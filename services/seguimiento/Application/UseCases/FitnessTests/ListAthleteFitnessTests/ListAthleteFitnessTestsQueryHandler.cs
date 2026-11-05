using Application.DTOs.FitnessTests;
using Application.Interfaces.Persistence;

namespace Application.UseCases.FitnessTests.ListAthleteFitnessTests;

public sealed class ListAthleteFitnessTestsQueryHandler(IFieldTestResultRepository repository)
{
    public async Task<IReadOnlyList<AthleteFitnessTestDto>> HandleAsync(
        ListAthleteFitnessTestsQuery query,
        CancellationToken cancellationToken = default)
    {
        var fromDateTime = query.From?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDateTime = query.To?.ToDateTime(new TimeOnly(23, 59, 59, 999), DateTimeKind.Utc);

        return await repository.ListByAthleteAndDateRangeAsync(
            query.AthleteId,
            query.TestId,
            fromDateTime,
            toDateTime,
            cancellationToken);
    }
}

