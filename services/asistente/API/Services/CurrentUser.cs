using Application.Interfaces.Services;

namespace API.Services;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    // Identidad sets this claim when issuing the token: a PF gets its own id, an athlete gets its PF's id.
    public const string PfIdClaim = "pf_id";
    private static readonly AsyncLocal<Guid?> AmbientPfId = new();

    public Guid? PfId =>
        AmbientPfId.Value ??
        (Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirst(PfIdClaim)?.Value, out var pfId)
            ? pfId
            : null);

    public IDisposable UsePfContext(Guid pfId)
    {
        AmbientPfId.Value = pfId;
        return new AmbientPfScope(() => AmbientPfId.Value = null);
    }

    private sealed class AmbientPfScope(Action onDispose) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                onDispose();
            }
        }
    }
}
