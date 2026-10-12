using Application.DTOs.Categories;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;
using Domain.Entities;

namespace Application.UseCases.Categories.CreateCategory;

public sealed record CreateCategoryCommand(Guid OrganizationId, string? Name, string? Description);

public sealed class CreateCategoryCommandHandler(
    IOrganizationRepository organizationRepository,
    ICategoryRepository categoryRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<CategoryDto> HandleAsync(
        CreateCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        // Valida que la organización exista y pertenezca al PF actual.
        var organization = await organizationRepository.GetByIdAsync(command.OrganizationId, cancellationToken);
        if (organization is null)
            throw new OrganizationNotFoundException();

        var category = Category.Create(command.OrganizationId, command.Name, command.Description);
        await categoryRepository.AddAsync(category, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CategoryMappings.ToDto(category);
    }
}
