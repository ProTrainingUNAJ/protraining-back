using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class AthleteRepository(AppDbContext context) : IAthleteRepository
{
    public async Task<IReadOnlyList<Athlete>> ListAsync(CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.Athletes.AsNoTracking().OrderBy(a => a.FullName).ToListAsync(cancellationToken));

    public async Task<Athlete?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.Athletes.FirstOrDefaultAsync(athlete => athlete.Id == id, cancellationToken));

    public Task AddAsync(Athlete athlete, CancellationToken cancellationToken = default)
    {
        context.Athletes.Add(athlete);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Athlete athlete, CancellationToken cancellationToken = default)
    {
        context.Athletes.Remove(athlete);
        return Task.CompletedTask;
    }
}
