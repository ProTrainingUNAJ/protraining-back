using Application.DTOs.FieldTests;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.FieldTests.ListFieldTestSessions;

// Resumen de sesiones por categoría: caso de uso de solo lectura (AsNoTracking
// en el repositorio, sin llamadas a otros servicios ni SaveChanges). Devuelve el DTO
// liviano (sin tests ni resultados) para que la pantalla que elige la sesión sea rápida.
public sealed class ListFieldTestSessionsQueryHandler(IFieldTestRepository repository)
{
    public async Task<IReadOnlyList<FieldTestSessionSummaryDto>> HandleAsync(
        ListFieldTestSessionsQuery query,
        CancellationToken cancellationToken = default)
    {
        var sessions = await repository.ListSessionsByCategoryAsync(query.CategoryId, cancellationToken);

        return sessions.Select(FieldTestMappings.ToSummaryDto).ToList();
    }
}
