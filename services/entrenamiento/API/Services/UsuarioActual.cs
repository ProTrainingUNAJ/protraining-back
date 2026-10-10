using Application.Interfaces.Services;

namespace API.Services;

public sealed class UsuarioActual(IHttpContextAccessor httpContextAccessor) : IUsuarioActual
{
    // Identidad pone este claim al emitir el token: para un PF es su id, para un atleta el de su PF.
    public const string ClaimPfId = "pf_id";

    public Guid? PfId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirst(ClaimPfId)?.Value, out var pfId)
            ? pfId
            : null;
}
