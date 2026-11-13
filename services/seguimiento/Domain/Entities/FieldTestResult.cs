using Domain.Common;

namespace Domain.Entities;

// Resultado de un atleta en un test de una sesión (KAN-424). El valor se guarda en la
// unidad del catálogo (CatalogTest.MeasurementUnit) y el intento distingue reintentos
// del mismo atleta en el mismo test. La unicidad (sesión, test, atleta, intento) la
// impone la base con su índice único.
public class FieldTestResult : BaseEntity
{
    public Guid SessionId { get; set; }
    public Guid CatalogTestId { get; set; }
    public Guid AthleteId { get; set; }
    public decimal Value { get; set; }
    public int Attempt { get; set; }
    public DateTime RecordedAt { get; set; }

    // KAN-568: instante en que la confirmación de 1RM se envió a Entrenamiento.
    // Si ya está seteado, confirmar de nuevo no vuelve a llamar (idempotencia).
    // El PUT de corrección lo limpia para permitir reconfirmar.
    public DateTime? ConfirmedAt { get; set; }
}
