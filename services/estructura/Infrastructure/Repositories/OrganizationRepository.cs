using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class OrganizationRepository(AppDbContext context) : IOrganizationRepository
{
    public async Task<IReadOnlyList<Organization>> ListAsync(CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.Organizations
                .AsNoTracking()
                .OrderBy(o => o.Name)
                .ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<(Organization Organization, int CategoriesCount, int AthletesCount)>> ListWithCountsAsync(CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
        {
            var results = await context.Organizations
                .AsNoTracking()
                .OrderBy(o => o.Name)
                .Select(o => new
                {
                    Organization = o,
                    CategoriesCount = context.Categories.Count(c => c.OrganizationId == o.Id),
                    AthletesCount = context.AthleteCategories
                        .Where(ac => context.Categories.Any(c => c.Id == ac.CategoryId && c.OrganizationId == o.Id))
                        .Select(ac => ac.AthleteId)
                        .Distinct()
                        .Count()
                })
                .ToListAsync(cancellationToken);

            return (IReadOnlyList<(Organization Organization, int CategoriesCount, int AthletesCount)>)results
                .Select(r => (r.Organization, r.CategoriesCount, r.AthletesCount))
                .ToList();
        });

    public async Task<(Organization Organization, int CategoriesCount, int AthletesCount)?> GetWithCountsByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync<(Organization Organization, int CategoriesCount, int AthletesCount)?>(async () =>
        {
            var result = await context.Organizations
                .AsNoTracking()
                .Where(o => o.Id == id)
                .Select(o => new
                {
                    Organization = o,
                    CategoriesCount = context.Categories.Count(c => c.OrganizationId == o.Id),
                    AthletesCount = context.AthleteCategories
                        .Where(ac => context.Categories.Any(c => c.Id == ac.CategoryId && c.OrganizationId == o.Id))
                        .Select(ac => ac.AthleteId)
                        .Distinct()
                        .Count()
                })
                .FirstOrDefaultAsync(cancellationToken);

            return result is null ? null : (result.Organization, result.CategoriesCount, result.AthletesCount);
        });

    public async Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.Organizations
                .FirstOrDefaultAsync(o => o.Id == id, cancellationToken));

    public Task AddAsync(Organization organization, CancellationToken cancellationToken = default)
    {
        context.Organizations.Add(organization);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Organization organization, CancellationToken cancellationToken = default)
    {
        context.Organizations.Remove(organization);
        return Task.CompletedTask;
    }
}
