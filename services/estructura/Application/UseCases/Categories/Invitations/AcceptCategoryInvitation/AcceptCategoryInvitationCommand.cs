using Application.DTOs.Athletes;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;
using Domain.Entities;

namespace Application.UseCases.Categories.Invitations.AcceptCategoryInvitation;

public sealed record AcceptCategoryInvitationCommand(
    string Token,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? EmergencyContact = null);

public sealed class AcceptCategoryInvitationCommandHandler(
    ICurrentUser currentUser,
    ICategoryInvitationRepository invitationRepository,
    IAthleteRepository athleteRepository,
    IAthleteCategoryRepository athleteCategoryRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<AthleteDto> HandleAsync(
        AcceptCategoryInvitationCommand command,
        CancellationToken cancellationToken = default)
    {
        var accountId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var userPfId = currentUser.PfId ?? throw new UnauthorizedAccessException();

        if (string.IsNullOrWhiteSpace(command.Token))
            throw new InvitationNotFoundException();

        var details = await invitationRepository.GetDetailsByTokenAsync(command.Token.Trim(), cancellationToken);
        if (details is null)
            throw new InvitationNotFoundException();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (details.Invitation.IsExpired(now))
            throw new InvitationExpiredException();

        // Rechazo si el pf_id del token no coincide con el de la invitación
        if (userPfId != details.Invitation.PfId)
            throw new ForbiddenException("El atleta no pertenece al preparador físico de la invitación.");

        // Idempotencia: Verificar si ya existe el perfil de Athlete para este usuario (sub)
        var athlete = await athleteRepository.GetByAccountIdAsync(accountId, cancellationToken);
        if (athlete is null)
        {
            athlete = Athlete.CreateAdult(
                command.FirstName,
                command.LastName,
                command.Email,
                command.Phone,
                signsInitialConsent: false,
                command.EmergencyContact);

            athlete.LinkAccount(accountId);
            await athleteRepository.AddAsync(athlete, cancellationToken);
        }

        // Idempotencia: Verificar si ya está asignado a la categoría
        var existingAssignment = await athleteCategoryRepository.GetAsync(athlete.Id, details.Invitation.CategoryId, cancellationToken);
        if (existingAssignment is null)
        {
            var athleteCategory = AthleteCategory.Assign(athlete.Id, details.Invitation.CategoryId);
            await athleteCategoryRepository.AddAsync(athleteCategory, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AthleteMappings.ToDto(athlete);
    }
}