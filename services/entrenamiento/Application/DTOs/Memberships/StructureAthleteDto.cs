namespace Application.DTOs.Memberships;

// Datos básicos del atleta consultados a Estructura por ID.
public sealed record StructureAthleteDto(Guid Id, string FullName);