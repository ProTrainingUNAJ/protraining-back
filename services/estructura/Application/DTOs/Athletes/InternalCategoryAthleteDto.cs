namespace Application.DTOs.Athletes;

/// <summary>
/// Atleta de una categoría para otros microservicios (Seguimiento: comparación con el grupo, KAN-578).
/// Solo el id y el nombre: sin datos de contacto ni médicos.
/// </summary>
public sealed record InternalCategoryAthleteDto(Guid Id, string FullName);
