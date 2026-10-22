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
