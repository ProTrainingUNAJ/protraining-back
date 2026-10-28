using System.Security.Cryptography;
using Application.Common.Security;
using Application.DTOs.Auth;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.Login;

/// <summary>
/// caso de uso que valida las credenciales del usuario y emite su token JWT
/// </summary>
public class LoginUseCase(
    UserManager<User> userManager,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenRepository refreshTokenRepository)
{
    /// <summary>
    /// ejecuta la logica de autenticacion de un usuario
    /// </summary>
    /// <param name="request">Credenciales enviadas (Email y Password).</param>
    /// <returns>LoginResponse con el JWT si es valido, o null si las credenciales son incorrectas.</returns>
    public async Task<LoginResponseDto> ExecuteAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        // 1. buscar al usuario por email
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null || !user.IsActive)
        {
            throw new InvalidCredentialsException();
        }

        // 2. verificar si la cuenta se encuentra bloqueada por intentos fallidos previos
        if (await userManager.IsLockedOutAsync(user))
        {
            throw new InvalidCredentialsException();
        }

        // 3. verificar la contrasena con el hasher seguro PBKDF2
        var isPasswordValid = await userManager.CheckPasswordAsync(user, request.Password);
        if (!isPasswordValid)
        {
            // suma un intento fallido. Al llegar a 5, Identity bloquea la cuenta automaticamente por 15 minutos
            await userManager.AccessFailedAsync(user);
            throw new InvalidCredentialsException();
        }

        // 4. si la contrasena fue correcta, reiniciar el contador de fallos a cero
        await userManager.ResetAccessFailedCountAsync(user);

        // 5. obtener roles y generar JWT centralizado con rol por defecto y duracion
        var roles = await userManager.GetRolesAsync(user);
        var generatedToken = jwtTokenGenerator.GenerateToken(user, roles);

        // 6. generar el token plano y persistir su hash SHA-256 para este dispositivo
        var refreshTokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = TokenHasher.Hash(refreshTokenValue),
            DeviceId = request.DeviceId,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow
        };
        await refreshTokenRepository.AddAsync(refreshToken, cancellationToken);
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);

        // 7. retornar la respuesta enviando el token original en texto plano al atleta
        return new LoginResponseDto(
            AccessToken: generatedToken.AccessToken,
            RefreshToken: refreshTokenValue,
            TokenType: "Bearer",
            ExpiresIn: generatedToken.ExpiresIn,
            User: new UserDto(
                user.Id,
                user.Email!,
                user.FullName,
                generatedToken.Role,
                user.PfId ?? user.Id,
                user.PhoneNumber
            )
        );

    }
}
