using Application.DTOs.FieldTests;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.FieldTests.UpdateFieldTestResult;

// Edición de un resultado cargado (KAN-425): 1 SELECT (sesión) + 1 SELECT (resultado
// dentro de esa sesión) + 1 UPDATE en un único SaveChangesAsync. Sesión ajena → 404;
// resultado ajeno o de otra sesión → 404. Si la corrección choca con el índice único
// (misma prueba, atleta e intento ya cargados) la base responde → 409.
public sealed class UpdateFieldTestResultCommandHandler(
    IFieldTestRepository repository,
    IUnitOfWork unitOfWork)
{
    public async Task<FieldTestResultDto> HandleAsync(
        UpdateFieldTestResultCommand command,
        CancellationToken cancellationToken = default)
    {
        // Sesión inexistente o de otro PF (filtro global) → 404.
        var session = await repository.GetSessionByIdAsync(command.SessionId, cancellationToken)
            ?? throw new FieldTestSessionNotFoundException();

        // Sesión cerrada → 400: la corrección también queda bloqueada (KAN-427).
        if (session.Status == FieldTestSessionStatus.Closed)
            throw new DomainException("La sesión ya está cerrada.");

        // Resultado inexistente, de otra sesión o de otro PF → 404.
        var result = await repository.GetResultAsync(command.SessionId, command.ResultId, cancellationToken)
            ?? throw new FieldTestResultNotFoundException();

        result.Value = command.Value;
        result.Attempt = command.Attempt;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return FieldTestMappings.ToDto(result);
    }
}
