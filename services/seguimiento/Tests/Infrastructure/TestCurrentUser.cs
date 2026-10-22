using Application.Interfaces.Services;

namespace Seguimiento.Tests.Infrastructure;

// Usuario de prueba configurable: simula el claim "pf_id" del JWT.
// Sin pfId se comporta como el arranque del servicio (sin token), que es
// exactamente el contexto en el que corre el CatalogTestSeeder.
public sealed class TestCurrentUser(Guid? pfId = null) : ICurrentUser
{
    public Guid? PfId { get; } = pfId;
}
