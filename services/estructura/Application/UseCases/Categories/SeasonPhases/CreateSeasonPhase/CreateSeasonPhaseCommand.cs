using Application.DTOs.Categories;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;
using Domain.Entities;

namespace Application.UseCases.Categories.SeasonPhases.CreateSeasonPhase;

public sealed record CreateSeasonPhaseCommand(
    Guid CategoryId,
    string Name,
    SeasonPhaseType Type,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed class CreateSeasonPhaseCommandHandler(
    ICategoryAccessValidator accessValidator,
    ISeasonPhaseRepository phaseRepository,
    ISeasonPhaseValidationService validationService,
    IUnitOfWork unitOfWork)
{
    public async Task<SeasonPhaseDto> HandleAsync(
        CreateSeasonPhaseCommand command,
        CancellationToken cancellationToken = default)
    {
        await accessValidator.ValidateWriteAccessAsync(command.CategoryId, cancellationToken);

        await validationService.ValidatePhaseDatesAsync(
            command.CategoryId,
            command.StartDate,
            command.EndDate,
            excludePhaseId: null,
            cancellationToken);

        var phase = SeasonPhase.Create(
            command.CategoryId,
            command.Name,
            command.Type,
            command.StartDate,
            command.EndDate);

        await phaseRepository.AddAsync(phase, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return CategoryMappings.ToDto(phase);
    }
}
