using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.FieldTests;

// Prueba del catálogo pedida al crear la sesión, con el orden en que se evalúa.
public sealed record FieldTestSessionTestInput
{
    [Required(ErrorMessage = "La prueba del catálogo es obligatoria.")]
    public Guid? CatalogTestId { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "El orden debe ser un número entero positivo.")]
    public int Order { get; init; }
}
