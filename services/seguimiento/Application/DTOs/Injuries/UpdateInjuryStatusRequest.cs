using System.ComponentModel.DataAnnotations;
using Domain.Entities;

namespace Application.DTOs.Injuries;

// Cambio de estado de una lesión (KAN-420). TransitionNotes es la observación clínica,
// obligatoria en toda transición según el contrato.
public sealed record UpdateInjuryStatusRequest
{
    [Required(ErrorMessage = "El estado nuevo es obligatorio.")]
    public InjuryStatus? NewStatus { get; init; }

    [Required(ErrorMessage = "La nota clínica es obligatoria.")]
    public string? TransitionNotes { get; init; }
}
