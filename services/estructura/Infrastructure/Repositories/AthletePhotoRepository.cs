using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class AthletePhotoRepository(AppDbContext context) : IAthletePhotoRepository
{
    public async Task<AthletePhoto?> GetByAthleteIdAsync(Guid athleteId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.AthletePhotos
                .FirstOrDefaultAsync(p => p.AthleteId == athleteId, cancellationToken));

    public Task AddAsync(AthletePhoto photo, CancellationToken cancellationToken = default)
    {
        context.AthletePhotos.Add(photo);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(AthletePhoto photo, CancellationToken cancellationToken = default)
    {
        context.AthletePhotos.Remove(photo);
        return Task.CompletedTask;
    }
}

