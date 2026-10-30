using Application.DTOs.Categories;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;

namespace Application.UseCases.Categories.Invitations.GenerateCategoryInvitationToken;

public sealed record GenerateCategoryInvitationTokenCommand(Guid CategoryId);

public sealed class GenerateCategoryInvitationTokenCommandHandler(
    ICategoryRepository categoryRepository,
    ICategoryInvitationRepository invitationRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<CategoryInvitationDto> HandleAsync(
        GenerateCategoryInvitationTokenCommand command,
        CancellationToken cancellationToken)
    {
        var category = await categoryRepository.GetByIdAsync(command.CategoryId, cancellationToken);
        if (category is null)
            throw new CategoryNotFoundException();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now.AddDays(7); // Criterio 1: expiracion 7 dias exactos

        var invitation = CategoryInvitation.Create(category.Id, expiresAt);
        await invitationRepository.AddAsync(invitation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CategoryInvitationDto(
            invitation.Token,
            invitation.ExpiresAt,
            category.Id,
            category.Name);
    }
}