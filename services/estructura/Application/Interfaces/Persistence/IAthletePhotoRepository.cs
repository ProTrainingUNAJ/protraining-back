using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IAthletePhotoRepository
{
    Task<AthletePhoto?> GetByAthleteIdAsync(Guid athleteId, CancellationToken cancellationToken = default);
    Task AddAsync(AthletePhoto photo, CancellationToken cancellationToken = default);
    Task DeleteAsync(AthletePhoto photo, CancellationToken cancellationToken = default);
}

