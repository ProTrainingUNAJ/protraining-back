using Domain.Common;

namespace Domain.Entities;

// Test del catálogo asociado a una sesión de test de campo (KAN-424), con el orden en el
// que se evalúa dentro de esa sesión.
public class FieldTestSessionTest : BaseEntity
{
    public Guid SessionId { get; set; }
    public Guid CatalogTestId { get; set; }
    public int Order { get; set; }
}
