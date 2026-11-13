using System.Security.Cryptography;
using Application.Common.Security;
using Application.DTOs.Auth;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.Refresh;

/// <summary>
/// caso de uso para renovar el token de acceso mediante la rotacion de un refresh token de 30 dias
/// </summary>
public class RefreshUseCase(
    IRefreshTokenRepository refreshTokenRepository,
    UserManager<User> userManager,
    IJwtTokenGenerator jwtTokenGenerator)
{
    public async Task<RefreshResponseDto> ExecuteAsync(RefreshRequestDto request, CancellationToken cancellationToken = default)
    {
        // 1. buscar el refresh token por su hash SHA-256 en la base de datos
        var tokenHash = TokenHasher.Hash(request.RefreshToken);
        var existingToken = await refreshTokenRepository.GetByTokenAsync(tokenHash, cancellationToken);

        if (existingToken == null)
        {
            throw new InvalidRefreshTokenException();
        }

        // 2. detectar reuso de token rotado (posible robo de sesion): revocar todos los tokens del dispositivo
        if (existingToken.IsRevoked && !string.IsNullOrEmpty(existingToken.ReplacedByToken))
        {
            await refreshTokenRepository.RevokeAllActiveByDeviceAsync(existingToken.UserId, existingToken.DeviceId, cancellationToken);
            throw new InvalidRefreshTokenException();
        }

        // 3. validar que pertenezca al mismo deviceId, no este revocado/expirado y el usuario este activo y desbloqueado
        if (existingToken.DeviceId != request.DeviceId ||
            existingToken.IsRevoked ||
            existingToken.IsExpired ||
            !existingToken.User.IsActive ||
            await userManager.IsLockedOutAsync(existingToken.User))
        {
            throw new InvalidRefreshTokenException();
        }

        // 4. rotacion del token: revocar el token actual
        existingToken.IsRevoked = true;
        existingToken.RevokedAt = DateTime.UtcNow;

        // 5. generar el nuevo refresh token plano y persistir los hashes SHA-256
        var newRefreshTokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var newRefreshTokenHash = TokenHasher.Hash(newRefreshTokenValue);

        existingToken.ReplacedByToken = newRefreshTokenHash;

        var newRefreshToken = new RefreshToken
        {
            UserId = existingToken.UserId,
            Token = newRefreshTokenHash,
            DeviceId = request.DeviceId,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow
        };

        await refreshTokenRepository.AddAsync(newRefreshToken, cancellationToken);
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);

        // 6. emitir nuevo access token centralizado con claims unificados
        var roles = await userManager.GetRolesAsync(existingToken.User);
        var generatedToken = jwtTokenGenerator.GenerateToken(existingToken.User, roles);

        // 7. retornar la respuesta con el nuevo token original en texto plano al atleta
        return new RefreshResponseDto(
            AccessToken: generatedToken.AccessToken,
            RefreshToken: newRefreshTokenValue,
            TokenType: "Bearer",
            ExpiresIn: generatedToken.ExpiresIn
        );
    }
}
