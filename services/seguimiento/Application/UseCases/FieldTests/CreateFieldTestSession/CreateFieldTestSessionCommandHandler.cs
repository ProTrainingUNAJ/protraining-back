using Application.DTOs.FieldTests;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.FieldTests.CreateFieldTestSession;

// Alta de la sesión con sus pruebas: 1 llamada a Estructura (categoría del PF)
// + 1 SELECT por prueba del catálogo + 1 INSERT en un único SaveChangesAsync. Categoría
// ajena o inexistente → 404; alguna prueba inexistente → 400; estado inicial Planned.
public sealed class CreateFieldTestSessionCommandHandler(
    IFieldTestRepository repository,
    ICatalogTestRepository catalogTestRepository,
    IStructureClient structureClient,
    IUnitOfWork unitOfWork)
{
    public async Task<FieldTestSessionDto> HandleAsync(
        CreateFieldTestSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        // Categoría inexistente o de otro PF (filtro global de Estructura) → 404.
        if (!await structureClient.IsOwnCategoryAsync(command.CategoryId, cancellationToken))
            throw new CategoryNotFoundException();

        if (command.Tests.Count == 0)
            throw new DomainException("La sesión debe incluir al menos una prueba del catálogo.");

        // La misma prueba no puede entrar dos veces en la misma sesión.
        if (command.Tests.Select(test => test.CatalogTestId).Distinct().Count() != command.Tests.Count)
            throw new DomainException("La sesión no puede repetir la misma prueba del catálogo.");

        // Cada prueba pedida debe existir en el catálogo del PF → 400 si alguna falta.
        foreach (var test in command.Tests)
        {
            _ = await catalogTestRepository.GetByIdAsync(test.CatalogTestId!.Value, cancellationToken)
                ?? throw new DomainException("Una o más pruebas del catálogo no existen.");
        }

        var session = new FieldTestSession
        {
            CategoryId = command.CategoryId,
            Date = command.Date,
            Name = command.Name,
            Status = FieldTestSessionStatus.Planned
        };

        var sessionTests = command.Tests
            .Select(test => new FieldTestSessionTest
            {
                SessionId = session.Id,
                CatalogTestId = test.CatalogTestId!.Value,
                Order = test.Order
            })
            .ToList();

        await repository.AddSessionAsync(session, sessionTests, cancellationToken);

        // Único SaveChanges: la sesión y sus pruebas quedan juntas o no queda nada.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return FieldTestMappings.ToDto(session, sessionTests, []);
    }
}
