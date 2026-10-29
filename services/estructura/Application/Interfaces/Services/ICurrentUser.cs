namespace Application.Interfaces.Services;

public interface ICurrentUser
{
    // PF that owns the data of the current request ("pf_id" claim of the token). Null without a valid token.
    Guid? PfId { get; }

    // Cuenta de Identidad del usuario (claim "sub" del token). Null sin un token válido.
    Guid? UserId { get; }

    // Rol principal del usuario en el token (claim "role"). Null sin un token válido.
    string? Role { get; }
}
