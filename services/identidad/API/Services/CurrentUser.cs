using Application.Interfaces.Services;

namespace API.Services;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    // Identidad sets this claim when issuing the token: a PF gets its own id, an athlete gets its PF's id.
    public const string PfIdClaim = "pf_id";

    public Guid? PfId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirst(PfIdClaim)?.Value, out var pfId)
            ? pfId
            : null;
}
