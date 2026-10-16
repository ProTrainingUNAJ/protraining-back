using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Estructura.Tests.Infrastructure;

public static class TestJwtGenerator
{
    public const string DefaultKey = "clave_local_de_desarrollo_cambiar_32+";
    public const string DefaultIssuer = "protraining-identidad";
    public const string DefaultAudience = "protraining";

    public static string GenerateToken(
        Guid pfId,
        Guid? userId = null,
        string role = "PreparadorFisico",
        string key = DefaultKey,
        string issuer = DefaultIssuer,
        string audience = DefaultAudience,
        TimeSpan? lifetime = null)
    {
        var resolvedUserId = userId ?? pfId;
        var expiration = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(1));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, resolvedUserId.ToString()),
            new(JwtRegisteredClaimNames.Email, $"pf-{pfId:N}@protraining.test"),
            new(JwtRegisteredClaimNames.Name, $"PF {pfId:N}"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("role", role),
            new("pf_id", pfId.ToString())
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiration,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string GenerateExpiredToken(Guid pfId, Guid? userId = null, string role = "PreparadorFisico") =>
        GenerateToken(pfId, userId, role, lifetime: TimeSpan.FromMinutes(-10));

    public static string GenerateTokenWithInvalidKey(Guid pfId, Guid? userId = null, string role = "PreparadorFisico") =>
        GenerateToken(pfId, userId, role, key: "clave_completamente_invalida_y_falsa_32+");
}
