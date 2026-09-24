using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ProTraining.Application.Common;

namespace ProTraining.Infrastructure.Auth;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? AccountId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var accountIdClaim = user?.FindFirst("AccountId")?.Value;
            if (Guid.TryParse(accountIdClaim, out var accountId))
                return accountId;

            // Si es atleta o PF, su propio NameIdentifier también puede ser su Id
            var idClaim = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(idClaim, out var userId))
                return userId;

            return null;
        }
    }

    public Guid? UserId
    {
        get
        {
            var idClaim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(idClaim, out var userId) ? userId : null;
        }
    }

    public string? Role => _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Role)?.Value;

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
}
