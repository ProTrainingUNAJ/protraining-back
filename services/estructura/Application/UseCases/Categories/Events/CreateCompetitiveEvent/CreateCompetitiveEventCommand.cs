using Application.DTOs.Categories;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;
using Domain.Entities;

namespace Application.UseCases.Categories.Events.CreateCompetitiveEvent;

public sealed record CreateCompetitiveEventCommand(
    Guid CategoryId,
    DateOnly Date,
    TimeOnly? StartTime,
    string Opponent,
    CompetitiveEventVenue Venue,
    CompetitiveEventType Type,
    string? Notes);

public sealed class CreateCompetitiveEventCommandHandler(
    ICategoryAccessValidator accessValidator,
    ICompetitiveEventRepository eventRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<CompetitiveEventDto> HandleAsync(
        CreateCompetitiveEventCommand command,
        CancellationToken cancellationToken = default)
    {
        await accessValidator.ValidateWriteAccessAsync(command.CategoryId, cancellationToken);

        var ev = CompetitiveEvent.Create(
            command.CategoryId,
            command.Date,
            command.Opponent,
            command.Venue,
            command.Type,
            command.StartTime,
            command.Notes);

        await eventRepository.AddAsync(ev, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return CategoryMappings.ToDto(ev);
    }
}
