namespace Application.DTOs.Athletes;

/// <summary>
/// DTO interno para comunicación entre microservicios (Estructura -> Asistente / Seguimiento).
/// Expone el Id del atleta junto con el PfId de su preparador físico, su categoría activa principal
/// y la lista de todas sus categorías activas (#182 · KAN-477).
/// </summary>
public sealed record InternalAthleteSummaryDto(
    Guid Id,
    Guid PfId,
    Guid? CategoryId,
    bool Active,
    IReadOnlyList<Guid>? CategoryIds = null);

