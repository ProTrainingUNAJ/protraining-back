using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Exceptions;

namespace Application.Services;

public sealed class SeasonPhaseValidationService(
    ICategoryRepository categoryRepository,
    ISeasonPhaseRepository seasonPhaseRepository) : ISeasonPhaseValidationService
{
    public async Task ValidatePhaseDatesAsync(
        Guid categoryId,
        DateOnly startDate,
        DateOnly endDate,
        Guid? excludePhaseId = null,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            throw new DomainException("La fecha de finalización no puede ser anterior a la fecha de inicio.");

        var category = await categoryRepository.GetByIdAsync(categoryId, cancellationToken);
        if (category is null)
            throw new CategoryNotFoundException();

        var hasOverlap = await seasonPhaseRepository.HasOverlappingPhaseAsync(
            categoryId,
            startDate,
            endDate,
            excludePhaseId,
            cancellationToken);

        if (hasOverlap)
            throw new DomainException("Las fechas de la fase se superponen con otra fase de la misma categoría.");
    }
}
