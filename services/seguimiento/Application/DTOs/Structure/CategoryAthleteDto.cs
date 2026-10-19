namespace Application.DTOs.Structure;

// Atleta de una categoría, con los campos mínimos que usa el semáforo del día.
// Estructura devuelve más datos (email, teléfono, etc.): se ignoran al deserializar.
public sealed class CategoryAthleteDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
}
