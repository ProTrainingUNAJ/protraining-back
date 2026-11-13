namespace Application.DTOs.FieldTests;

// Resultado de un atleta en un test de la sesión (KAN-425). El valor viaja en la
// unidad del catálogo y el intento distingue reintentos del mismo atleta.
public sealed record FieldTestResultDto(
    Guid Id,
    Guid CatalogTestId,
    Guid AthleteId,
    decimal Value,
    int Attempt,
    DateTime RecordedAt);
