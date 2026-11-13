using Domain.Common;

namespace Domain.Entities;

// Corrección manual de asistencia del PF (KAN-445): cuando un atleta figura "sin registro"
// (no cargó su entrenamiento del día), el PF puede marcarlo como presente o ausente.
public class Attendance : BaseEntity
{
    public Guid AthleteId { get; set; }
    public DateTime Date { get; set; }
    public bool IsPresent { get; set; }
}