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
