using Application.DTOs.Categories;

namespace Application.DTOs.Athletes;

// El atleta logueado, resuelto a partir del token. Lo usan otros servicios para saber
// qué atleta de Estructura es el usuario (check-in, rutina de hoy).
public sealed record MeDto(
    Guid AthleteId,
    string FirstName,
    string LastName,
    string FullName,
    bool Active,
    IReadOnlyList<CategoryDto> Categories);
