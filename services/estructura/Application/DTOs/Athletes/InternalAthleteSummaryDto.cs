namespace Application.DTOs.Athletes;

/// <summary>
/// DTO interno para comunicación entre microservicios (Estructura -> Asistente / Seguimiento).
/// Expone el Id del atleta junto con el PfId de su preparador físico y su categoría activa.
/// </summary>
public sealed record InternalAthleteSummaryDto(
    Guid Id,
    Guid PfId,
    Guid? CategoryId,
    bool Active);

