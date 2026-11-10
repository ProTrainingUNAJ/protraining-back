using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.FieldTests;

// Alta de una sesión de test de campo (KAN-427): el PF elige su categoría, la fecha,
// el nombre y las pruebas del catálogo en orden de evaluación. La categoría debe ser
// suya (404 si no) y todas las pruebas deben existir (400 si no).
public sealed record CreateFieldTestSessionRequest
{
    [Required(ErrorMessage = "La categoría es obligatoria.")]
    public Guid? CategoryId { get; init; }

    [Required(ErrorMessage = "La fecha es obligatoria.")]
    public DateTime? Date { get; init; }

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(150, ErrorMessage = "El nombre no puede superar los 150 caracteres.")]
    public string? Name { get; init; }

    [Required(ErrorMessage = "Las pruebas del catálogo son obligatorias.")]
    public List<FieldTestSessionTestInput>? Tests { get; init; }
}
