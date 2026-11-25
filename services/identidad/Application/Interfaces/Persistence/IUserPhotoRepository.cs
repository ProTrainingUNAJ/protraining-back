using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IUserPhotoRepository
{
    Task<UserPhoto?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(UserPhoto photo, CancellationToken cancellationToken = default);
    Task DeleteAsync(UserPhoto photo, CancellationToken cancellationToken = default);
}

