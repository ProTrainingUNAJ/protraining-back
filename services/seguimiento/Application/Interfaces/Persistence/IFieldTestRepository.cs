using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IFieldTestRepository
{
    // Sesión por id: el filtro global del AppDbContext la limita al PF del token
    // (null = inexistente o de otro PF → 404 en el caso de uso).
    Task<FieldTestSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken cancellationToken = default);

    // Sesión tracked (sin AsNoTracking): el caso de uso cambia el estado y lo guarda
    // con el mismo SaveChanges. Mismo filtro global → null → 404.
    Task<FieldTestSession?> GetSessionByIdForUpdateAsync(Guid sessionId, CancellationToken cancellationToken = default);

    // Sesiones de una categoría del PF, de la más reciente a la más antigua.
    Task<IReadOnlyList<FieldTestSession>> ListSessionsByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);

    // Tests del catálogo asociados a la sesión, en el orden de evaluación.
    Task<IReadOnlyList<FieldTestSessionTest>> ListSessionTestsAsync(Guid sessionId, CancellationToken cancellationToken = default);

    // Resultados cargados en la sesión (vista de campo del GET).
    Task<IReadOnlyList<FieldTestResult>> ListResultsBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    // Resultado dentro de la sesión pedida: id ajeno u otra sesión → null → 404.
    Task<FieldTestResult?> GetResultAsync(Guid sessionId, Guid resultId, CancellationToken cancellationToken = default);

    Task AddResultAsync(FieldTestResult result, CancellationToken cancellationToken = default);

    // Alta de la sesión con sus pruebas: solo encola las filas; el alta se confirma con
    // el único SaveChangesAsync del caso de uso.
    Task AddSessionAsync(
        FieldTestSession session,
        IReadOnlyList<FieldTestSessionTest> tests,
        CancellationToken cancellationToken = default);
}
