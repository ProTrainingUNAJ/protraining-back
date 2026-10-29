using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Application.UseCases.Categories.SeasonPhases.DeleteSeasonPhase;

public sealed record DeleteSeasonPhaseCommand(Guid CategoryId, Guid Id);

public sealed class DeleteSeasonPhaseCommandHandler(
    ICategoryAccessValidator accessValidator,
    ISeasonPhaseRepository phaseRepository,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(
        DeleteSeasonPhaseCommand command,
        CancellationToken cancellationToken = default)
    {
        await accessValidator.ValidateWriteAccessAsync(command.CategoryId, cancellationToken);

        var phase = await phaseRepository.GetByIdAsync(command.Id, cancellationToken);
        if (phase is null || phase.CategoryId != command.CategoryId)
        {
            throw new SeasonPhaseNotFoundException();
        }

        await phaseRepository.DeleteAsync(phase, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
