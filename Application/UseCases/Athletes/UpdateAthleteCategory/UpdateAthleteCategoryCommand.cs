using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;

namespace Application.UseCases.Athletes.UpdateAthleteCategory;

public sealed record UpdateAthleteCategoryCommand(long Id, string? Category);

public sealed class UpdateAthleteCategoryCommandHandler(IAthleteRepository repository, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(UpdateAthleteCategoryCommand command, CancellationToken cancellationToken = default)
    {
        Athlete.ValidateId(command.Id);
        var athlete = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        athlete.SetCategory(command.Category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
