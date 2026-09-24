using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IAthleteRepository
{
    Task<IReadOnlyList<Athlete>> ListAsync(CancellationToken cancellationToken = default);
    Task<Athlete?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task AddAsync(Athlete athlete, CancellationToken cancellationToken = default);
    Task DeleteAsync(Athlete athlete, CancellationToken cancellationToken = default);
}
