namespace Application.Interfaces.Services;

public interface IUsuarioActual
{
    // PF dueño de los datos del pedido (claim "pf_id" del token). Null si no hay token válido.
    Guid? PfId { get; }
}
