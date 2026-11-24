using Application.DTOs.Athletes;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;
using Domain.Entities;

namespace Application.UseCases.Athletes.CreateAthlete;

public sealed record CreateAthleteCommand(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    bool IsMinor,
    string? GuardianName = null,
    string? GuardianEmail = null,
    string? GuardianPhone = null,
    bool AllowsPhysicalTraining = true,
    bool AllowsHabitsRecommendations = false,
    string? EmergencyContact = null,
    string? TelegramUsername = null);

public sealed class CreateAthleteCommandHandler(IAthleteRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<AthleteDto> HandleAsync(CreateAthleteCommand command, CancellationToken cancellationToken = default)
    {
        var athlete = command.IsMinor
            ? Athlete.CreateMinor(
                command.FirstName,
                command.LastName,
                command.Email,
                command.Phone,
                command.GuardianName,
                command.GuardianEmail,
                command.GuardianPhone,
                command.AllowsPhysicalTraining,
                command.AllowsHabitsRecommendations,
                command.EmergencyContact,
                telegramUsername: command.TelegramUsername)
            : Athlete.CreateAdult(
                command.FirstName,
                command.LastName,
                command.Email,
                command.Phone,
                signsInitialConsent: false,
                command.EmergencyContact,
                telegramUsername: command.TelegramUsername);

        // Sin esto el PF podía cargar dos veces al mismo atleta con el mismo correo (KAN-660)
        if (await repository.EmailExistsAsync(athlete.Email, cancellationToken: cancellationToken))
            throw new ConflictException(AthleteEmailMessages.AlreadyExists);

        await repository.AddAsync(athlete, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AthleteMappings.ToDto(athlete);
    }
}

public static class AthleteEmailMessages
{
    public const string AlreadyExists = "Ya existe un usuario registrado con este correo electrónico.";
}
