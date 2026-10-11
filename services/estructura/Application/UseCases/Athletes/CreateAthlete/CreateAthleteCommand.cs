using Application.DTOs;
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
    string? MedicalConditions = null);

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
                command.MedicalConditions)
            : Athlete.CreateAdult(
                command.FirstName,
                command.LastName,
                command.Email,
                command.Phone,
                signsInitialConsent: true,
                command.EmergencyContact,
                command.MedicalConditions);

        await repository.AddAsync(athlete, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AthleteMappings.ToDto(athlete);
    }
}
