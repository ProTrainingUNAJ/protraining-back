using Domain.Entities;

namespace Application.DTOs.FieldTests;

// Cambio de estado de la sesión: solo se admite Planned → InProgress → Closed
// (el enum viaja como texto, igual que en el resto del contrato). El enum restringe los
// valores posibles; la secuencia la valida el caso de uso → 400.
public sealed record UpdateFieldTestSessionStatusRequest
{
    public FieldTestSessionStatus Status { get; init; }
}
