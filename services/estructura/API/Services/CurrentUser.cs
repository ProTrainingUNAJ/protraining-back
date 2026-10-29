using System;
using System.Security.Claims;
using System.Threading;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Http;

namespace API.Services;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public const string PfIdClaim = "pf_id";
    private static readonly AsyncLocal<Guid?> AmbientPfId = new();

    public Guid? PfId =>
        AmbientPfId.Value ??
        (Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirst(PfIdClaim)?.Value, out var pfId)
            ? pfId
            : null);

    public Guid? UserId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value, out var userId)
            ? userId
            : null;

    public string? Role =>
        httpContextAccessor.HttpContext?.User.FindFirst("role")?.Value;

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
