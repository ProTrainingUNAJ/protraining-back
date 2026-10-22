using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Application.Interfaces.Services;
using Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Services;

/// <summary>
/// servicio que genera los tokens JWT autofirmados con los claims de la plataforma
/// </summary>
public class JwtTokenGenerator(IConfiguration configuration) : IJwtTokenGenerator
{
    public string GenerateToken(User user, string role)
    {
        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Falta la variable de configuración Jwt:Key.");

        var issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Falta la variable de configuración Jwt:Issuer.");

        var audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Falta la variable de configuración Jwt:Audience.");

        // para un PF es su propio Id, para un atleta es el Id de su PF
        var pfId = user.PfId ?? user.Id;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Name, user.FullName),
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
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    public GeneratedToken GenerateToken(User user, IEnumerable<string> roles)
    {
        var primaryRole = roles.FirstOrDefault() ?? IJwtTokenGenerator.DefaultRole;
        var token = GenerateToken(user, primaryRole);
        return new GeneratedToken(token, primaryRole, IJwtTokenGenerator.DefaultExpirationSeconds);
    }

}
