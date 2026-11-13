using System.Security.Cryptography;
using Application.Common.Security;
using Application.DTOs.Auth;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.GoogleLogin;

/// <summary>
/// Caso de uso que autentica a un usuario existente mediante Google ID Token (KAN-541).
/// </summary>
public class GoogleLoginUseCase(
    UserManager<User> userManager,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenRepository refreshTokenRepository,
    IGoogleTokenValidator googleTokenValidator)
{
    private const string GoogleProvider = "Google";

    public async Task<LoginResponseDto> ExecuteAsync(
        GoogleLoginRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            throw new DomainException("El token de Google es obligatorio.");
        }

        // 1. Validar criptográficamente el token de Google
        GoogleTokenPayload payload;
        try
        {
            payload = await googleTokenValidator.ValidateAsync(request.IdToken.Trim(), cancellationToken);
        }
        catch (Exception)
        {
            throw new InvalidCredentialsException("El token de Google es inválido o ha expirado.");
        }

        if (string.IsNullOrWhiteSpace(payload.Subject) || string.IsNullOrWhiteSpace(payload.Email))
        {
            throw new InvalidCredentialsException("El token de Google no contiene la información de identidad requerida.");
        }

        // 2. Exigir correo verificado por Google
        if (!payload.EmailVerified)
        {
            throw new InvalidCredentialsException("El correo de Google no está verificado.");
        }

        var cleanEmail = payload.Email.Trim().ToLowerInvariant();

        // 3. Buscar si la cuenta de Google ya está vinculada mediante su identificador estable (sub)
        var user = await userManager.FindByLoginAsync(GoogleProvider, payload.Subject);

        if (user != null)
        {
            // Validar que la cuenta esté activa y no bloqueada
            if (!user.IsActive)
            {
                throw new InvalidCredentialsException("No encontramos una cuenta habilitada para iniciar sesión con Google.");
            }

            if (await userManager.IsLockedOutAsync(user))
            {
                throw new InvalidCredentialsException("La cuenta se encuentra bloqueada.");
            }
        }
        else
        {
            // 4. Si no está vinculada, buscar por correo (vinculación automática controlada)
            user = await userManager.FindByEmailAsync(cleanEmail);

            // Restricción obligatoria: NO registrar usuarios nuevos automáticamente
            if (user == null || !user.IsActive)
            {
                throw new InvalidCredentialsException("No encontramos una cuenta habilitada para iniciar sesión con Google.");
            }

            if (await userManager.IsLockedOutAsync(user))
            {
                throw new InvalidCredentialsException("La cuenta se encuentra bloqueada.");
            }

            // Verificar que el usuario no tenga ya otra identidad de Google vinculada
            var existingLogins = await userManager.GetLoginsAsync(user);
            var existingGoogleLogin = existingLogins.FirstOrDefault(l => l.LoginProvider == GoogleProvider);
            if (existingGoogleLogin != null && existingGoogleLogin.ProviderKey != payload.Subject)
            {
                throw new InvalidCredentialsException("La cuenta ya está vinculada a otra identidad de Google.");
            }

            // Registrar el vínculo de forma persistente en user_logins
            var addLoginResult = await userManager.AddLoginAsync(
                user,
                new UserLoginInfo(GoogleProvider, payload.Subject, GoogleProvider));

            if (!addLoginResult.Succeeded)
            {
                throw new DomainException("No se pudo vincular la cuenta de Google.");
            }
        }

        // 5. Reiniciar contador de intentos fallidos
        await userManager.ResetAccessFailedCountAsync(user);

        // 6. Obtener roles y emitir JWT propio de ProTraining
        var roles = await userManager.GetRolesAsync(user);
        var generatedToken = jwtTokenGenerator.GenerateToken(user, roles);

        // 7. Generar y persistir refresh token de 30 días
        var deviceId = string.IsNullOrWhiteSpace(request.DeviceId)
            ? Guid.NewGuid().ToString()
            : request.DeviceId.Trim();

        var refreshTokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = TokenHasher.Hash(refreshTokenValue),
            DeviceId = deviceId,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow
        };

        await refreshTokenRepository.AddAsync(refreshToken, cancellationToken);
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);

        // 8. Retornar respuesta compatible con el login tradicional
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
                user.PhoneNumber,
                user.MustChangePassword
            )
        );
    }
}
