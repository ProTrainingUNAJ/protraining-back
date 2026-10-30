using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class CategoryInvitationRepository(AppDbContext context) : ICategoryInvitationRepository
{
    public async Task AddAsync(CategoryInvitation invitation, CancellationToken cancellationToken) =>
        await context.CategoryInvitations.AddAsync(invitation, cancellationToken);

    public async Task<CategoryInvitation?> GetByTokenAsync(string token, CancellationToken cancellationToken) =>
        await context.CategoryInvitations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Token == token && i.DeletedAt == null, cancellationToken);

    public async Task<CategoryInvitationDetails?> GetDetailsByTokenAsync(string token, CancellationToken cancellationToken) =>
        await context.CategoryInvitations
            .IgnoreQueryFilters()
            .Where(i => i.Token == token && i.DeletedAt == null)
            .Select(i => new CategoryInvitationDetails(
                i,
                context.Categories
                    .IgnoreQueryFilters()
                    .Where(c => c.Id == i.CategoryId && c.DeletedAt == null)
                    .Select(c => c.Name)
                    .FirstOrDefault() ?? string.Empty
            ))
            .FirstOrDefaultAsync(cancellationToken);
}