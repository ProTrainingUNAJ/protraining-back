using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class FieldTestRepository(AppDbContext context) : IFieldTestRepository
{
    // El filtro global por PfId del AppDbContext vuelve invisible la sesión de otro PF:
    // null → 404 sin revelar que el id exista.
    public async Task<FieldTestSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.FieldTestSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken));

    // Sin AsNoTracking: el caso de uso cambia el estado y lo guarda con el mismo
    // SaveChanges. El filtro global deja fuera las sesiones de otro PF.
    public async Task<FieldTestSession?> GetSessionByIdForUpdateAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.FieldTestSessions
                .FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken));

    public async Task<IReadOnlyList<FieldTestSession>> ListSessionsByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.FieldTestSessions
                .AsNoTracking()
                .Where(session => session.CategoryId == categoryId)
                .OrderByDescending(session => session.Date)
                .ThenByDescending(session => session.CreatedAt)
                .ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<FieldTestSessionTest>> ListSessionTestsAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.FieldTestSessionTests
                .AsNoTracking()
                .Where(test => test.SessionId == sessionId)
                .OrderBy(test => test.Order)
                .ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<FieldTestResult>> ListResultsBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.FieldTestResults
                .AsNoTracking()
                .Where(result => result.SessionId == sessionId)
                .OrderBy(result => result.AthleteId)
                .ThenBy(result => result.CatalogTestId)
                .ThenBy(result => result.Attempt)
                .ToListAsync(cancellationToken));

    // Sin AsNoTracking: el caso de uso corrige el resultado y lo guarda con el mismo
    // SaveChanges. El filtro global deja fuera los resultados de otro PF.
    public async Task<FieldTestResult?> GetResultAsync(Guid sessionId, Guid resultId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.FieldTestResults
                .FirstOrDefaultAsync(result => result.Id == resultId && result.SessionId == sessionId, cancellationToken));

    // Solo encola la fila: el alta se confirma con el único SaveChangesAsync del caso de uso,
    // donde el índice único traduce el duplicado a 409.
    public Task AddResultAsync(FieldTestResult result, CancellationToken cancellationToken = default)
    {
        context.FieldTestResults.Add(result);
        return Task.CompletedTask;
    }

    // Encola la sesión y sus pruebas juntas: el alta se confirma con el único
    // SaveChangesAsync del caso de uso.
    public Task AddSessionAsync(
        FieldTestSession session,
        IReadOnlyList<FieldTestSessionTest> tests,
        CancellationToken cancellationToken = default)
    {
        context.FieldTestSessions.Add(session);
        context.FieldTestSessionTests.AddRange(tests);
        return Task.CompletedTask;
    }
}
