using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;

namespace Application.UseCases.Athletes.UpdateMyPhysicalProfile;

public sealed record UpdateMyPhysicalProfileCommand(
    decimal? WeightKg,
    string? AthleticGoal,
    DateOnly? BirthDate = null,
    string? Gender = null);

public sealed record UpdateMyPhysicalProfileResponseDto(
    decimal? WeightKg,
    string AthleticGoal,
    DateTime? PhysicalProfileUpdatedAt,
    DateOnly? BirthDate = null,
    string? Gender = null);

/// <summary>
/// Autoservicio del atleta autenticado: actualiza peso y objetivo desde el token, sin ids en la ruta.
/// </summary>
public sealed class UpdateMyPhysicalProfileCommandHandler(
    ICurrentUser currentUser,
    IAthleteRepository athleteRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<UpdateMyPhysicalProfileResponseDto> HandleAsync(
        UpdateMyPhysicalProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        var accountId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var athlete = await athleteRepository.GetByAccountIdAsync(accountId, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        athlete.UpdatePhysicalProfile(command.WeightKg, command.AthleticGoal);
        if (command.BirthDate.HasValue || command.Gender is not null)
        {
            athlete.UpdatePersonalInfo(command.BirthDate, command.Gender);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateMyPhysicalProfileResponseDto(
            athlete.WeightKg,
            AthleticGoalLabels.ToLabel(athlete.AthleticGoal),
            athlete.PhysicalProfileUpdatedAt,
            athlete.BirthDate,
            athlete.Gender);
    }
}
