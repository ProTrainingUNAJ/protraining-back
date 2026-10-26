using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace API.Services;

public interface IServiceTokenFactory
{
    string CreateToken();
}

public sealed class ServiceTokenFactory(IConfiguration configuration) : IServiceTokenFactory
{
    public const string ServiceRole = "Servicio";

    public string CreateToken()
    {
        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Falta la variable de configuración Jwt__Key.");

        if (Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException("Jwt__Key debe tener al menos 32 caracteres.");
        }

        var issuer = configuration["Jwt:Issuer"] ?? "protraining-estructura";
        var audience = configuration["Jwt:Audience"] ?? "protraining";

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "estructura"),
            new(JwtRegisteredClaimNames.Name, "Estructura"),
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
