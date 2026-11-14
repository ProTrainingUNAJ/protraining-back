using Application.DTOs.CheckIns;

namespace Application.Interfaces.Persistence;

public interface ICheckInInjuryPainRepository
{
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<CheckInInjuryPainDto>>> ListByCheckInIdsAsync(
        IReadOnlyCollection<Guid> checkInIds,
        CancellationToken cancellationToken = default);

    Task SetForCheckInAsync(
        Guid checkInId,
        IReadOnlyCollection<(Guid InjuryId, int Level)> pains,
        CancellationToken cancellationToken = default);
}

