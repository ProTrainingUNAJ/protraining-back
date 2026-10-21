using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Seguimiento.Tests.Infrastructure;

// Emite JWT firmados con la misma configuración que valida AddJwtAuthentication de la API
// (MapInboundClaims = false → claims crudos "name" y "role", como en producción).
// Patrón copiado de los tests de Estructura, adaptado a los claims que usa Seguimiento (KAN-367).
public static class TestJwtGenerator
{
    public const string DefaultKey = "clave_local_de_desarrollo_cambiar_32+";
    public const string DefaultIssuer = "protraining-seguimiento";
    public const string DefaultAudience = "protraining";

    public static string GenerateToken(Guid pfId, string role = "PreparadorFisico", TimeSpan? lifetime = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, pfId.ToString()),
            new(JwtRegisteredClaimNames.Name, $"PF {pfId:N}"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("role", role),
            new("pf_id", pfId.ToString()),
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(DefaultKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: DefaultIssuer,
            audience: DefaultAudience,
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(1)),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
