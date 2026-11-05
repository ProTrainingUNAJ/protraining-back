namespace Application.DTOs.FieldTests;

// Sesión de test de campo con sus tests y resultados (KAN-425): el front móvil pinta
// la pantalla de carga con un solo GET. El estado viaja como texto ("Planned",
// "InProgress", "Closed"), igual que en el resto del contrato.
public sealed record FieldTestSessionDto(
    Guid Id,
    Guid CategoryId,
    DateTime Date,
    string Name,
    string Status,
    IReadOnlyList<FieldTestSessionTestDto> Tests,
    IReadOnlyList<FieldTestResultDto> Results);
