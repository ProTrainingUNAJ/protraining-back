using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ProTraining.Domain.Entities;

namespace ProTraining.Infrastructure.Auth;

public class JwtService
{
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;

    public JwtService(IConfiguration configuration)
    {
        _secretKey = configuration["Jwt:Secret"] ?? "ProTraining_Default_Super_Secret_Key_For_Local_Dev_123456!";
        _issuer = configuration["Jwt:Issuer"] ?? "ProTrainingAuth";
        _audience = configuration["Jwt:Audience"] ?? "ProTrainingApp";
    }

    public string GenerateToken(Account account, Guid? athleteId = null)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_secretKey);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Email, account.Email),
            new(ClaimTypes.Name, account.FullName),
            new(ClaimTypes.Role, account.Role.ToString()),
            new("AccountId", account.Id.ToString())
        };

        if (athleteId.HasValue)
        {
            claims.Add(new Claim("AthleteId", athleteId.Value.ToString()));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddDays(7),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
