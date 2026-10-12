using System.Security.Cryptography;
using Application.DTOs.Auth;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.RefreshToken;

/// <summary>
/// caso de uso para renovar el token de acceso mediante la rotacion de un refresh token de 30 dias
/// </summary>
public class RefreshTokenUseCase(
    IRefreshTokenRepository refreshTokenRepository,
    UserManager<User> userManager,
    IJwtTokenGenerator jwtTokenGenerator)
{
    public async Task<RefreshResponseDto> ExecuteAsync(RefreshRequestDto request, CancellationToken cancellationToken = default)
    {
        // 1. buscar el refresh token en la base de datos
        var existingToken = await refreshTokenRepository.GetByTokenAsync(request.RefreshToken, cancellationToken);

        // 2. validar que el token exista, pertenezca al mismo deviceId y este activo (no revocado ni expirado)
        if (existingToken == null ||
            existingToken.DeviceId != request.DeviceId ||
            existingToken.IsRevoked ||
            existingToken.IsExpired ||
            !existingToken.User.IsActive)
        {
            throw new InvalidRefreshTokenException();
        }

        // 3. rotacion del token: revocar el token actual
        existingToken.IsRevoked = true;
        existingToken.RevokedAt = DateTime.UtcNow;

        // 4. generar el nuevo refresh token criptografico de 30 dias
        var newRefreshTokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        existingToken.ReplacedByToken = newRefreshTokenValue;

        var newRefreshToken = new Domain.Entities.RefreshToken
        {
            UserId = existingToken.UserId,
            Token = newRefreshTokenValue,
            DeviceId = request.DeviceId,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow
        };

        await refreshTokenRepository.AddAsync(newRefreshToken, cancellationToken);
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);

        // 5. emitir nuevo access token (JWT de 1 hora) con claims unificados
        var roles = await userManager.GetRolesAsync(existingToken.User);
        var primaryRole = roles.FirstOrDefault() ?? "Atleta";
        var newAccessToken = jwtTokenGenerator.GenerateToken(existingToken.User, primaryRole);

        // 6. retornar la respuesta con ambos tokens
        return new RefreshResponseDto(
            AccessToken: newAccessToken,
            RefreshToken: newRefreshTokenValue,
            TokenType: "Bearer",
            ExpiresIn: 3600
        );
    }
}
