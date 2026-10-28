using System.Collections.Generic;
using Domain.Entities;

namespace Application.Interfaces.Services;

/// <summary>
/// resultado de la emision de un token JWT con su rol resuelto y duracion
/// </summary>
public record GeneratedToken(string AccessToken, string Role, int ExpiresIn);

/// <summary>
/// contrato para el generador de tokens JWT autofirmados
/// </summary>
public interface IJwtTokenGenerator
{
    const string DefaultRole = "Atleta";
    const int DefaultExpirationSeconds = 3600;

    string GenerateToken(User user, string role);
    GeneratedToken GenerateToken(User user, IEnumerable<string> roles);
}
