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
}
