namespace Application.DTOs.Memberships;

// Categoría a la que pertenece el atleta, tal como la devuelve Estructura.
public sealed record AthleteCategoryDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string? Description);
