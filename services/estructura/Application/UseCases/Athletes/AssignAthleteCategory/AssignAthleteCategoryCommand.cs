using Application.DTOs.Categories;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;
using Domain.Entities;

namespace Application.UseCases.Athletes.AssignAthleteCategory;

public sealed record AssignAthleteCategoryCommand(Guid AthleteId, Guid CategoryId);

public sealed class AssignAthleteCategoryCommandHandler(
    IAthleteRepository athleteRepository,
    ICategoryRepository categoryRepository,
    IAthleteCategoryRepository athleteCategoryRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<CategoryDto> HandleAsync(
        AssignAthleteCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        // Valida que el atleta pertenezca al PF actual.
        var athlete = await athleteRepository.GetByIdAsync(command.AthleteId, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        // Valida que la categoría pertenezca al PF actual.
        var category = await categoryRepository.GetByIdAsync(command.CategoryId, cancellationToken);
        if (category is null)
            throw new CategoryNotFoundException();

        // Idempotente: si ya existe el vínculo, no hace nada.
        var existing = await athleteCategoryRepository.GetAsync(command.AthleteId, command.CategoryId, cancellationToken);
        if (existing is not null)
            return CategoryMappings.ToDto(category);

        var link = AthleteCategory.Assign(command.AthleteId, command.CategoryId);
        await athleteCategoryRepository.AddAsync(link, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CategoryMappings.ToDto(category);
    }
}
