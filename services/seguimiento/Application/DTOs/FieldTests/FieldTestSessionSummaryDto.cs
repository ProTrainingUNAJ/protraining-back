namespace Application.DTOs.FieldTests;

// Resumen de sesión para el listado por categoría: sin tests ni resultados
// para no pesar el payload de la pantalla que elige la sesión. Estado como texto.
public sealed record FieldTestSessionSummaryDto(
    Guid Id,
    Guid CategoryId,
    DateTime Date,
    string Name,
    string Status);
