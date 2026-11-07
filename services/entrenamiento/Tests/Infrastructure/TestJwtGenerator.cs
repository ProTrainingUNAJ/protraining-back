using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Entrenamiento.Tests.Infrastructure;

public static class TestJwtGenerator
{
    public const string DefaultKey = "protraining-entrenamiento-super-secret-key-for-tests-only-min-32-chars!";
    public const string DefaultIssuer = "protraining-entrenamiento";
    public const string DefaultAudience = "protraining";

    public static string GenerateToken(
        Guid? pfId = null,
        string role = "PreparadorFisico",
        Guid? userId = null,
        string? email = null)
    {
        var resolvedUserId = userId ?? Guid.NewGuid();
        var resolvedPfId = pfId ?? Guid.NewGuid();
        var resolvedEmail = email ?? $"pf-{resolvedPfId:N}@protraining.test";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, resolvedUserId.ToString()),
            new(ClaimTypes.Email, resolvedEmail),
            new(ClaimTypes.Role, role),
            new("role", role),
            new("sub", resolvedUserId.ToString()),
            new("pf_id", resolvedPfId.ToString()),
            new("email", resolvedEmail)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(DefaultKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: DefaultIssuer,
            audience: DefaultAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
