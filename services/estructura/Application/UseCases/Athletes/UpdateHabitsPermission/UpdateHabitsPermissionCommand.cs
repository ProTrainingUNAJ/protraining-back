using Application.DTOs;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Athletes.UpdateHabitsPermission;

public sealed record UpdateHabitsPermissionCommand(Guid AthleteId, bool AllowsHabitsRecommendations);

public sealed class UpdateHabitsPermissionCommandHandler(IAthleteRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<AthleteDto> HandleAsync(UpdateHabitsPermissionCommand command, CancellationToken cancellationToken = default)
    {
        var athlete = await repository.GetByIdAsync(command.AthleteId, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        athlete.UpdateHabitsPermission(command.AllowsHabitsRecommendations);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AthleteMappings.ToDto(athlete);
    }
}
