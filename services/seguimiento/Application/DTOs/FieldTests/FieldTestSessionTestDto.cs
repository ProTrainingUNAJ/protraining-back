namespace Application.DTOs.FieldTests;

// Test del catálogo incluido en la sesión, con su orden de evaluación (KAN-425).
public sealed record FieldTestSessionTestDto(
    Guid CatalogTestId,
    int Order);
