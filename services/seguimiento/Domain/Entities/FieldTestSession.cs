using Domain.Common;

namespace Domain.Entities;

// Sesión de testeo de una categoría (KAN-424): agrupa los tests del catálogo que entran
// en la evaluación (p. ej. "Evaluación de pretemporada") y los resultados de cada atleta.
public class FieldTestSession : BaseEntity
{
    public Guid CategoryId { get; set; }
    public DateTime Date { get; set; }
    public string Name { get; set; } = string.Empty;
    public FieldTestSessionStatus Status { get; set; } = FieldTestSessionStatus.Planned;
}
