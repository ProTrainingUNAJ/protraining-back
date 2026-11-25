using Application.Interfaces.Persistence;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data;

public sealed class UserPhotoRepository(AppDbContext context) : IUserPhotoRepository
{
    public async Task<UserPhoto?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.UserPhotos
                .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken));

    public async Task AddAsync(UserPhoto photo, CancellationToken cancellationToken = default)
    {
        await context.UserPhotos.AddAsync(photo, cancellationToken);
    }

    public Task DeleteAsync(UserPhoto photo, CancellationToken cancellationToken = default)
    {
        context.UserPhotos.Remove(photo);
        return Task.CompletedTask;
    }
}

