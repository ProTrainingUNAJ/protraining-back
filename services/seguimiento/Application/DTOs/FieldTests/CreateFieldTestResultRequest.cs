using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.FieldTests;

// Carga rápida de un resultado en la sesión (KAN-425). El atleta debe ser del PF
// (se valida contra Estructura) y la unicidad (sesión, prueba, atleta, intento) la
// impone la base con su índice único → 409 si ya fue cargado.
public sealed record CreateFieldTestResultRequest
{
    [Required(ErrorMessage = "El test del catálogo es obligatorio.")]
    public Guid? CatalogTestId { get; init; }

    [Required(ErrorMessage = "El atleta es obligatorio.")]
    public Guid? AthleteId { get; init; }

    [Required(ErrorMessage = "El valor del test es obligatorio.")]
    public decimal? Value { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "El intento debe ser un número entero positivo.")]
    public int Attempt { get; init; }
}
