using Application.DTOs.Categories;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;
using Domain.Entities;

namespace Application.UseCases.Categories.Events.UpdateCompetitiveEvent;

public sealed record UpdateCompetitiveEventCommand(
    Guid CategoryId,
    Guid Id,
    DateOnly Date,
    TimeOnly? StartTime,
    string Opponent,
    CompetitiveEventVenue Venue,
    CompetitiveEventType Type,
    string? Notes);

public sealed class UpdateCompetitiveEventCommandHandler(
    ICategoryAccessValidator accessValidator,
    ICompetitiveEventRepository eventRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<CompetitiveEventDto> HandleAsync(
        UpdateCompetitiveEventCommand command,
        CancellationToken cancellationToken = default)
    {
        await accessValidator.ValidateWriteAccessAsync(command.CategoryId, cancellationToken);

        var ev = await eventRepository.GetByIdAsync(command.Id, cancellationToken);
        if (ev is null || ev.CategoryId != command.CategoryId)
        {
            throw new CompetitiveEventNotFoundException();
        }

        ev.Reschedule(command.Date, command.StartTime);
        ev.UpdateDetails(command.Opponent, command.Venue, command.Type, command.Notes);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return CategoryMappings.ToDto(ev);
    }
}
