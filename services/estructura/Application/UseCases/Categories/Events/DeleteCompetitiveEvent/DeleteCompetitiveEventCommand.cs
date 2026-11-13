using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Application.UseCases.Categories.Events.DeleteCompetitiveEvent;

public sealed record DeleteCompetitiveEventCommand(Guid CategoryId, Guid Id);

public sealed class DeleteCompetitiveEventCommandHandler(
    ICategoryAccessValidator accessValidator,
    ICompetitiveEventRepository eventRepository,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(
        DeleteCompetitiveEventCommand command,
        CancellationToken cancellationToken = default)
    {
        await accessValidator.ValidateWriteAccessAsync(command.CategoryId, cancellationToken);

        var ev = await eventRepository.GetByIdAsync(command.Id, cancellationToken);
        if (ev is null || ev.CategoryId != command.CategoryId)
        {
            throw new CompetitiveEventNotFoundException();
        }

        await eventRepository.DeleteAsync(ev, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
