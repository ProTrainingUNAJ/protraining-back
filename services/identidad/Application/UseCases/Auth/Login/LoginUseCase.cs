using System.Security.Cryptography;
using Application.DTOs.Auth;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Application.Exceptions;

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
    /// <returns>LoginResponse con el JWT si es válido, o null si las credenciales son incorrectas.</returns>
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


        // 3. verificar la contraseña con el hasher seguro PBKDF2
        var isPasswordValid = await userManager.CheckPasswordAsync(user, request.Password);
        if (!isPasswordValid)
        {
            // suma un intento fallido. Al llegar a 5, Identity bloquea la cuenta automáticamente por 15 minutos
            await userManager.AccessFailedAsync(user);
            throw new InvalidCredentialsException();
        }

        // 4. si la contraseña fue correcta, reiniciar el contador de fallos a cero
        await userManager.ResetAccessFailedCountAsync(user);


        // 5. obtener el rol del usuario (por defecto Atleta si no tiene)
        var roles = await userManager.GetRolesAsync(user);
        var primaryRole = roles.FirstOrDefault() ?? "Atleta";

        // 6. generar el JWT con claims unificados (sub, email, role, pf_id)
        var token = jwtTokenGenerator.GenerateToken(user, primaryRole);

        // 7. generar y persistir el refresh token criptografico de 30 dias para este dispositivo
        var refreshTokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshToken = new Domain.Entities.RefreshToken
        {
            UserId = user.Id,
            Token = refreshTokenValue,
            DeviceId = request.DeviceId,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow
        };
        await refreshTokenRepository.AddAsync(refreshToken, cancellationToken);
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);
        // 8. retornar la respuesta con ambos tokens
        return new LoginResponseDto(
            AccessToken: token,
            RefreshToken: refreshTokenValue,
            TokenType: "Bearer",
            ExpiresIn: 3600,
            User: new UserDto(
                user.Id,
                user.Email!,
                user.FullName,
                primaryRole,
                user.PfId ?? user.Id
            )
        );
    }
}
