using Application.DTOs.FitnessTests;

namespace Application.Interfaces.Persistence;

public interface IFieldTestResultRepository
{
    Task<IReadOnlyList<AthleteFitnessTestDto>> ListByAthleteAndDateRangeAsync(
        Guid athleteId,
        Guid? catalogTestId = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default);
}

