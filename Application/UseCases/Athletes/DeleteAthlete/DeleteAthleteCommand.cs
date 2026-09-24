using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;

namespace Application.UseCases.Athletes.DeleteAthlete;

public sealed record DeleteAthleteCommand(long Id);

public sealed class DeleteAthleteCommandHandler(IAthleteRepository repository, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(DeleteAthleteCommand command, CancellationToken cancellationToken = default)
    {
        Athlete.ValidateId(command.Id);
        var athlete = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        await repository.DeleteAsync(athlete, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
