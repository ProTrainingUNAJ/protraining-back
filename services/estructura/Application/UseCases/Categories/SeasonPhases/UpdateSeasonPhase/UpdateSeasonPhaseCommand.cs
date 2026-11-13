using Application.DTOs.Categories;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;
using Domain.Entities;

namespace Application.UseCases.Categories.SeasonPhases.UpdateSeasonPhase;

public sealed record UpdateSeasonPhaseCommand(
    Guid CategoryId,
    Guid Id,
    string Name,
    SeasonPhaseType Type,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed class UpdateSeasonPhaseCommandHandler(
    ICategoryAccessValidator accessValidator,
    ISeasonPhaseRepository phaseRepository,
    ISeasonPhaseValidationService validationService,
    IUnitOfWork unitOfWork)
{
    public async Task<SeasonPhaseDto> HandleAsync(
        UpdateSeasonPhaseCommand command,
        CancellationToken cancellationToken = default)
    {
        await accessValidator.ValidateWriteAccessAsync(command.CategoryId, cancellationToken);

        var phase = await phaseRepository.GetByIdAsync(command.Id, cancellationToken);
        if (phase is null || phase.CategoryId != command.CategoryId)
        {
            throw new SeasonPhaseNotFoundException();
        }

        await validationService.ValidatePhaseDatesAsync(
            command.CategoryId,
            command.StartDate,
            command.EndDate,
            excludePhaseId: command.Id,
            cancellationToken);

        phase.UpdateDetails(command.Name, command.Type);
        phase.UpdateDateRange(command.StartDate, command.EndDate);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return CategoryMappings.ToDto(phase);
    }
}
