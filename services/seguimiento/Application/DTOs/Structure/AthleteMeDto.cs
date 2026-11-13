namespace Application.DTOs.Structure;

public sealed class CategoryMeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

// Atleta logueado, tal como lo devuelve Estructura en GET /api/v1/me.
public sealed class AthleteMeDto
{
    public Guid AthleteId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public IReadOnlyList<CategoryMeDto> Categories { get; set; } = [];
}
