using Application.DTOs.FieldTests;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.FieldTests.GetFieldTestSession;

// Caso de uso de lectura (KAN-425): solo consulta y mapea a DTO (AsNoTracking en el
// repositorio), sin llamadas a otros servicios. Sesión ajena o inexistente → 404.
public sealed class GetFieldTestSessionQueryHandler(IFieldTestRepository repository)
{
    public async Task<FieldTestSessionDto> HandleAsync(
        GetFieldTestSessionQuery query,
        CancellationToken cancellationToken = default)
    {
        var session = await repository.GetSessionByIdAsync(query.SessionId, cancellationToken)
            ?? throw new FieldTestSessionNotFoundException();

        var tests = await repository.ListSessionTestsAsync(query.SessionId, cancellationToken);
        var results = await repository.ListResultsBySessionAsync(query.SessionId, cancellationToken);

        return FieldTestMappings.ToDto(session, tests, results);
    }
}
