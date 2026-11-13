using Domain.Entities;

namespace Application.Interfaces.Persistence;

public sealed record CategoryInvitationDetails(
    CategoryInvitation Invitation,
    string CategoryName
);

public interface ICategoryInvitationRepository
{
    Task AddAsync(CategoryInvitation invitation, CancellationToken cancellationToken);
    Task<CategoryInvitation?> GetByTokenAsync(string token, CancellationToken cancellationToken);
    Task<CategoryInvitationDetails?> GetDetailsByTokenAsync(string token, CancellationToken cancellationToken);
}