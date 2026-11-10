using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Services;

public interface IServiceTokenFactory
{
    string CreateToken();
}

/// <summary>
/// Emite tokens JWT con rol 'Servicio' para autenticar llamadas internas entre Gateway e Identidad (#KAN-539).
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
            ?? throw new InvalidOperationException("Falta la variable de configuración Jwt:Audience.");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "gateway"),
            new(JwtRegisteredClaimNames.Name, "Gateway"),
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
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
