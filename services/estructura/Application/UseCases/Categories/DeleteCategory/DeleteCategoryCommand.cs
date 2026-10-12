using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Categories.DeleteCategory;

public sealed record DeleteCategoryCommand(Guid Id);

public sealed class DeleteCategoryCommandHandler(
    ICategoryRepository repository,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(
        DeleteCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        var category = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (category is null)
            throw new CategoryNotFoundException();

        var hasAthletes = await repository.HasAthletesAsync(command.Id, cancellationToken);
        if (hasAthletes)
            throw new ConflictException("No se puede eliminar la categoría porque tiene atletas asignados.");

        await repository.DeleteAsync(category, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
