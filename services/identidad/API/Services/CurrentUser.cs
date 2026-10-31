using System.Security.Claims;
using Application.Interfaces.Services;

namespace API.Services;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    // Identidad agrega este claim al emitir el token: un PF recibe su propio id, un atleta el id de su PF.
    public const string PfIdClaim = "pf_id";

    public Guid? PfId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirst(PfIdClaim)?.Value, out var pfId)
            ? pfId
            : null;

    public Guid? UserId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            // ASP.NET puede mapear "sub" a NameIdentifier según la configuración del JWT.
            var sub = user?.FindFirst("sub")?.Value ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(sub, out var userId) ? userId : null;
        }
    }
}