namespace Application.DTOs.Structure;

// Atleta logueado, tal como lo devuelve Estructura en GET /api/v1/me.
public sealed class AthleteMeDto
{
    public Guid AthleteId { get; set; }
    public string FullName { get; set; } = string.Empty;
}