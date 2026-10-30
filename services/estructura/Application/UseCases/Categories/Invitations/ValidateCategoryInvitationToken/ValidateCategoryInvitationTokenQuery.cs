using Application.DTOs.Categories;
using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Categories.Invitations.ValidateCategoryInvitationToken;

public sealed record ValidateCategoryInvitationTokenQuery(string Token);

public sealed class ValidateCategoryInvitationTokenQueryHandler(
    ICategoryInvitationRepository invitationRepository,
    TimeProvider timeProvider)
{
    public async Task<CategoryInvitationDto> HandleAsync(
        ValidateCategoryInvitationTokenQuery query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Token))
            throw new InvitationNotFoundException();

        var details = await invitationRepository.GetDetailsByTokenAsync(query.Token.Trim(), cancellationToken);
        if (details is null)
            throw new InvitationNotFoundException();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (details.Invitation.IsExpired(now))
            throw new InvitationExpiredException(); // Criterio 2: HTTP 410 Gone

        return new CategoryInvitationDto(
            details.Invitation.Token,
            details.Invitation.ExpiresAt,
            details.Invitation.CategoryId,
            details.CategoryName);
    }
}