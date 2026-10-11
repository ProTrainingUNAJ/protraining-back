using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Athletes.DeleteAthlete;

public sealed record DeleteAthleteCommand(Guid Id);

public sealed class DeleteAthleteCommandHandler(IAthleteRepository repository, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(DeleteAthleteCommand command, CancellationToken cancellationToken = default)
    {
        var athlete = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        await repository.DeleteAsync(athlete, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
