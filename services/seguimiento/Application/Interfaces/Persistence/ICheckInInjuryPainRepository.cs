using Application.DTOs.CheckIns;

namespace Application.Interfaces.Persistence;

public interface ICheckInInjuryPainRepository
{
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<CheckInInjuryPainDto>>> ListByCheckInIdsAsync(
        IReadOnlyCollection<Guid> checkInIds,
        CancellationToken cancellationToken = default);

    // Pico de dolor (máximo nivel registrado) por molestia.
    Task<IReadOnlyDictionary<Guid, int>> GetPeakPainByInjuryIdsAsync(
        IReadOnlyCollection<Guid> injuryIds,
        CancellationToken cancellationToken = default);

    Task SetForCheckInAsync(
        Guid checkInId,
        IReadOnlyCollection<(Guid InjuryId, int Level)> pains,
        CancellationToken cancellationToken = default);
}

