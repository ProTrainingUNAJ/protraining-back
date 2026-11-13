using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Injuries;

// Alta de una lesión (KAN-419). Zone y Description son obligatorias; las fechas se
// validan en el dominio: la de retorno es obligatoria y posterior a la de la lesión.
public sealed record CreateAthleteInjuryRequest
{
    [Required(ErrorMessage = "La zona de la lesión es obligatoria.")]
    public string Zone { get; init; } = string.Empty;

    [Required(ErrorMessage = "La descripción de la lesión es obligatoria.")]
    public string Description { get; init; } = string.Empty;

    public DateTime InjuryDate { get; init; }

    public DateTime? EstimatedReturnDate { get; init; }
}
