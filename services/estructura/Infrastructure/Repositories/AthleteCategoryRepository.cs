using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class AthleteCategoryRepository(AppDbContext context) : IAthleteCategoryRepository
{
    public async Task<IReadOnlyList<AthleteCategory>> ListByAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.AthleteCategories
                .AsNoTracking()
                .Where(ac => ac.AthleteId == athleteId)
                .ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<Athlete>> ListAthletesByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.AthleteCategories
                .AsNoTracking()
                .Where(ac => ac.CategoryId == categoryId)
                .Join(context.Athletes.Include(a => a.DigitalConsent),
                    ac => ac.AthleteId,
                    a => a.Id,
                    (ac, a) => a)
                .OrderBy(a => a.LastName)
                .ThenBy(a => a.FirstName)
                .ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<Category>> ListCategoriesByAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.AthleteCategories
                .AsNoTracking()
                .Where(ac => ac.AthleteId == athleteId)
                .Join(context.Categories,
                    ac => ac.CategoryId,
                    c => c.Id,
                    (ac, c) => c)
                .OrderBy(c => c.Name)
                .ToListAsync(cancellationToken));

    public async Task<AthleteCategory?> GetAsync(Guid athleteId, Guid categoryId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.AthleteCategories
                .FirstOrDefaultAsync(ac => ac.AthleteId == athleteId && ac.CategoryId == categoryId, cancellationToken));

    public Task AddAsync(AthleteCategory athleteCategory, CancellationToken cancellationToken = default)
    {
        context.AthleteCategories.Add(athleteCategory);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(AthleteCategory athleteCategory, CancellationToken cancellationToken = default)
    {
        context.AthleteCategories.Remove(athleteCategory);
        return Task.CompletedTask;
    }

    public async Task<bool> HasAthletesByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.AthleteCategories
                .Join(context.Categories,
                    ac => ac.CategoryId,
                    c => c.Id,
                    (ac, c) => c.OrganizationId)
                .AnyAsync(orgId => orgId == organizationId, cancellationToken));
}
