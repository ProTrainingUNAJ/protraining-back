namespace Application.Interfaces.Services;

public interface ICurrentUser
{
    // PF dueño de los datos del request actual (claim "pf_id" del token). Null sin un token válido.
    Guid? PfId { get; }

    // Id de la cuenta en sí (claim "sub"). Para un atleta NO es el PfId: el PfId es el id de su PF.
    Guid? UserId { get; }
}