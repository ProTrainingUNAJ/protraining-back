using Application.DTOs.Athletes;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.Categories.Invitations.AcceptCategoryInvitation;

public sealed record AcceptCategoryInvitationCommand(
    string Token,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    DateOnly BirthDate = default,
    string? GuardianName = null,
    string? GuardianEmail = null,
    string? GuardianPhone = null,
    bool AllowsPhysicalTraining = true,
    bool AllowsHabitsRecommendations = false,
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
            if (command.BirthDate == default)
                throw new DomainException("La fecha de nacimiento es obligatoria.");

            // Cálculo de minoría de edad según fecha actual en hora de Argentina (KAN-401)
            var argentinaNow = ArgentinaTime.FromUtc(now);
            var today = DateOnly.FromDateTime(argentinaNow);
            var age = today.Year - command.BirthDate.Year;
            if (command.BirthDate > today.AddYears(-age))
            {
                age--;
            }
            var isMinor = age < 18;

            if (isMinor)
            {
                // Menor de edad: validamos datos de tutor y consentimiento obligatorio
                if (string.IsNullOrWhiteSpace(command.GuardianName))
                    throw new DomainException("El nombre del tutor es obligatorio para atletas menores de edad.");

                var hasEmail = !string.IsNullOrWhiteSpace(command.GuardianEmail);
                var hasPhone = !string.IsNullOrWhiteSpace(command.GuardianPhone);
                if (!hasEmail && !hasPhone)
                    throw new DomainException("Debe proporcionar al menos un medio de contacto del tutor (correo electrónico o teléfono).");

                if (!command.AllowsPhysicalTraining)
                    throw new DomainException("El tutor legal debe autorizar obligatoriamente la preparación física.");

                athlete = Athlete.CreateMinor(
                    command.FirstName,
                    command.LastName,
                    command.Email,
                    command.Phone,
                    command.GuardianName,
                    command.GuardianEmail,
                    command.GuardianPhone,
                    command.AllowsPhysicalTraining,
                    command.AllowsHabitsRecommendations,
                    command.EmergencyContact);
            }
            else
            {
                // Mayor de edad: consentimiento estándar sin tutor (se firma luego en /me/consent, KAN-326)
                athlete = Athlete.CreateAdult(
                    command.FirstName,
                    command.LastName,
                    command.Email,
                    command.Phone,
                    signsInitialConsent: false,
                    command.EmergencyContact);
            }

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