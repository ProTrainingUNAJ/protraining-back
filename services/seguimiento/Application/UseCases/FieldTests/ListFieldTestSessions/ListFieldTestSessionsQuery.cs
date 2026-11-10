namespace Application.UseCases.FieldTests.ListFieldTestSessions;

// Listado de las sesiones de test de campo de una categoría: solo lectura,
// el filtro global del AppDbContext deja únicamente las del PF del token.
public sealed record ListFieldTestSessionsQuery(Guid CategoryId);
