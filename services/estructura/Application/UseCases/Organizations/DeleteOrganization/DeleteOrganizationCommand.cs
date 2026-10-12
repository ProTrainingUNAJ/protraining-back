using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Organizations.DeleteOrganization;

public sealed record DeleteOrganizationCommand(Guid Id);

public sealed class DeleteOrganizationCommandHandler(
    IOrganizationRepository repository,
    IAthleteCategoryRepository athleteCategoryRepository,
    ICategoryRepository categoryRepository,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(
        DeleteOrganizationCommand command,
        CancellationToken cancellationToken = default)
    {
        var organization = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (organization is null)
            throw new OrganizationNotFoundException();

        var hasAthletes = await athleteCategoryRepository.HasAthletesByOrganizationAsync(command.Id, cancellationToken);
        if (hasAthletes)
            throw new ConflictException("No se puede eliminar la organización porque tiene atletas asignados a sus categorías.");

        var hasCategories = (await categoryRepository.ListByOrganizationAsync(command.Id, cancellationToken)).Count > 0;
        if (hasCategories)
            throw new ConflictException("No se puede eliminar la organización porque tiene categorías asociadas.");

        await repository.DeleteAsync(organization, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
