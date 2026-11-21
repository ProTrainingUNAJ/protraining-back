using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class OrganizationPhotoRepository(AppDbContext context) : IOrganizationPhotoRepository
{
    public async Task<OrganizationPhoto?> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.OrganizationPhotos
                .FirstOrDefaultAsync(p => p.OrganizationId == organizationId, cancellationToken));

    public Task AddAsync(OrganizationPhoto photo, CancellationToken cancellationToken = default)
    {
        context.OrganizationPhotos.Add(photo);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(OrganizationPhoto photo, CancellationToken cancellationToken = default)
    {
        context.OrganizationPhotos.Remove(photo);
        return Task.CompletedTask;
    }
}

