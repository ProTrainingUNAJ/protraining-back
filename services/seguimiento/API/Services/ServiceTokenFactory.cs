using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace API.Services;

/// <summary>
/// Fábrica de tokens JWT con rol 'Servicio' (mismo esquema que Asistente, KAN-472).
/// Seguimiento la usa para los endpoints internos de Estructura (KAN-578).
/// </summary>
public sealed class ServiceTokenFactory(IConfiguration configuration) : IServiceTokenFactory
{
    public const string ServiceRole = "Servicio";

    public string CreateToken()
    {
        var key = configuration["Jwt:Key"]
            ?? configuration["Jwt__Key"]
            ?? throw new InvalidOperationException("Falta la variable de configuración Jwt:Key.");

        if (Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException("Jwt:Key debe tener al menos 32 caracteres.");
        }

        var issuer = configuration["Jwt:Issuer"]
            ?? configuration["Jwt__Issuer"]
            ?? throw new InvalidOperationException("Falta la variable de configuración Jwt:Issuer.");

        var audience = configuration["Jwt:Audience"]
            ?? configuration["Jwt__Audience"]
            ?? "protraining";

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "seguimiento"),
            new(JwtRegisteredClaimNames.Name, "Seguimiento"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("role", ServiceRole)
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

