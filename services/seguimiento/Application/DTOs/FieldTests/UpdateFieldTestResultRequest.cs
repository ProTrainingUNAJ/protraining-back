using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.FieldTests;

// Corrección de un resultado ya cargado (KAN-425): el front reenvía valor e intento
// completos; un intento repetido para la misma prueba y atleta choca con el índice
// único → 409.
public sealed record UpdateFieldTestResultRequest
{
    [Required(ErrorMessage = "El valor del test es obligatorio.")]
    public decimal? Value { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "El intento debe ser un número entero positivo.")]
    public int Attempt { get; init; }
}
