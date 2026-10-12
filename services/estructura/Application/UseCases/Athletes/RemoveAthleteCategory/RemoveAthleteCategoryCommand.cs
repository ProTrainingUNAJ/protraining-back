using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Athletes.RemoveAthleteCategory;

public sealed record RemoveAthleteCategoryCommand(Guid AthleteId, Guid CategoryId);

public sealed class RemoveAthleteCategoryCommandHandler(
    IAthleteRepository athleteRepository,
    IAthleteCategoryRepository athleteCategoryRepository,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(
        RemoveAthleteCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        // Valida que el atleta pertenezca al PF actual.
        var athlete = await athleteRepository.GetByIdAsync(command.AthleteId, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        var link = await athleteCategoryRepository.GetAsync(command.AthleteId, command.CategoryId, cancellationToken);
        if (link is null)
            throw new CategoryNotFoundException();

        await athleteCategoryRepository.DeleteAsync(link, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
