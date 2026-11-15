using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class CategoryRepository(AppDbContext context) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> ListByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.Categories
                .AsNoTracking()
                .Where(c => c.OrganizationId == organizationId)
                .OrderBy(c => c.Name)
                .ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<(Category Category, int AthletesCount)>> ListWithAthletesCountAsync(Guid? organizationId = null, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
        {
            var query = context.Categories.AsNoTracking();
            if (organizationId.HasValue && organizationId.Value != Guid.Empty)
            {
                query = query.Where(c => c.OrganizationId == organizationId.Value);
            }

            var results = await query
                .OrderBy(c => c.Name)
                .Select(c => new
                {
                    Category = c,
                    AthletesCount = context.AthleteCategories.Count(ac => ac.CategoryId == c.Id)
                })
                .ToListAsync(cancellationToken);

            return (IReadOnlyList<(Category Category, int AthletesCount)>)results
                .Select(r => (r.Category, r.AthletesCount))
                .ToList();
        });

    public async Task<(Category Category, int AthletesCount)?> GetWithAthletesCountByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync<(Category Category, int AthletesCount)?>(async () =>
        {
            var result = await context.Categories
                .AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new
                {
                    Category = c,
                    AthletesCount = context.AthleteCategories.Count(ac => ac.CategoryId == c.Id)
                })
                .FirstOrDefaultAsync(cancellationToken);

            return result is null ? null : (result.Category, result.AthletesCount);
        });

    public async Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.Categories
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken));

    public Task AddAsync(Category category, CancellationToken cancellationToken = default)
    {
        context.Categories.Add(category);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Category category, CancellationToken cancellationToken = default)
    {
        context.Categories.Remove(category);
        return Task.CompletedTask;
    }

    public async Task<bool> HasAthletesAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.AthleteCategories
                .AnyAsync(ac => ac.CategoryId == categoryId, cancellationToken));
}
