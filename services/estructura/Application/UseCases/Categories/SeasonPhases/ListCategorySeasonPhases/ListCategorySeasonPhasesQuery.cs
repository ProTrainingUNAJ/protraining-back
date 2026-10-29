using Application.DTOs.Categories;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;

namespace Application.UseCases.Categories.SeasonPhases.ListCategorySeasonPhases;

public sealed record ListCategorySeasonPhasesQuery(Guid CategoryId);

public sealed class ListCategorySeasonPhasesQueryHandler(
    ICategoryAccessValidator accessValidator,
    ISeasonPhaseRepository phaseRepository)
{
    public async Task<IReadOnlyList<SeasonPhaseDto>> HandleAsync(
        ListCategorySeasonPhasesQuery query,
        CancellationToken cancellationToken = default)
    {
        await accessValidator.ValidateReadAccessAsync(query.CategoryId, cancellationToken);

        var phases = await phaseRepository.ListByCategoryAsync(query.CategoryId, cancellationToken);

        return phases.Select(CategoryMappings.ToDto).ToList();
    }
}
