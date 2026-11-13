using Application.DTOs.FieldTests;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.FieldTests.CreateFieldTestResult;

// Camino caliente de la carga en cancha (KAN-425): 1 SELECT (sesión) + 1 SELECT (pruebas
// de la sesión, KAN-568) + 1 llamada a Estructura (atleta del PF) + 1 INSERT, todo
// confirmado con un único SaveChangesAsync. Sesión ajena → 404; prueba que no forma parte
// de la sesión → 400; atleta ajeno → 404 sin escribir nada; el duplicado (sesión, prueba,
// atleta, intento) lo rechaza el índice único de la base → 409.
public sealed class CreateFieldTestResultCommandHandler(
    IFieldTestRepository repository,
    IStructureClient structureClient,
    IUnitOfWork unitOfWork)
{
    public async Task<FieldTestResultDto> HandleAsync(
        CreateFieldTestResultCommand command,
        CancellationToken cancellationToken = default)
    {
        // Sesión inexistente o de otro PF (filtro global) → 404.
        var session = await repository.GetSessionByIdAsync(command.SessionId, cancellationToken)
            ?? throw new FieldTestSessionNotFoundException();

        // Sesión cerrada → 400: la toma de marcas terminó y nada más se carga.
        if (session.Status == FieldTestSessionStatus.Closed)
            throw new DomainException("La sesión ya está cerrada.");

        // KAN-568 CA2: solo se guardan resultados de pruebas que formen parte de la
        // sesión. Reutiliza ListSessionTestsAsync (filtro global → solo vinculación
        // activa); cualquier otra prueba → 400 sin escribir nada.
        var sessionTests = await repository.ListSessionTestsAsync(command.SessionId, cancellationToken);
        if (!sessionTests.Any(test => test.CatalogTestId == command.CatalogTestId))
            throw new DomainException("La prueba del catálogo no pertenece a esta sesión.");

        // Atleta inexistente o de otro PF → 404; no se persiste ningún resultado.
        if (!await structureClient.IsOwnAthleteAsync(command.AthleteId, cancellationToken))
            throw new AthleteNotFoundException();

        var result = new FieldTestResult
        {
            SessionId = command.SessionId,
            CatalogTestId = command.CatalogTestId,
            AthleteId = command.AthleteId,
            Value = command.Value,
            Attempt = command.Attempt,
            RecordedAt = DateTime.UtcNow
        };

        await repository.AddResultAsync(result, cancellationToken);

        // Único SaveChanges: un duplicado revienta en el índice único → PersistenceConflictException.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return FieldTestMappings.ToDto(result);
    }
}
