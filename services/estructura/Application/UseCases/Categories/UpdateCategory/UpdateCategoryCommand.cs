using Application.DTOs.Categories;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Categories.UpdateCategory;

public sealed record UpdateCategoryCommand(Guid Id, string? Name, string? Description);

public sealed class UpdateCategoryCommandHandler(
    ICategoryRepository repository,
    IUnitOfWork unitOfWork)
{
    public async Task<CategoryDto> HandleAsync(
        UpdateCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        var category = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (category is null)
            throw new CategoryNotFoundException();

        category.Rename(command.Name);
        category.UpdateDescription(command.Description);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CategoryMappings.ToDto(category);
    }
}
