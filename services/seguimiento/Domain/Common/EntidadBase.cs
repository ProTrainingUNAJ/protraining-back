namespace Domain.Common;

// Base de toda entidad del servicio. Id se genera al crear la entidad;
// PfId, CreadoEn y BorradoEn los completa el AppDbContext al guardar.
public abstract class EntidadBase
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PfId { get; private set; }
    public DateTime CreadoEn { get; private set; }
    public DateTime? BorradoEn { get; private set; }
}
