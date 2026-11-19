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
/// Caso de uso que autentica únicamente a un usuario existente que ya tenga Google vinculado explícitamente (KAN-541).
/// No crea usuarios nuevos ni vincula cuentas automáticamente por correo.
/// </summary>
public class GoogleLoginUseCase(
    UserManager<User> userManager,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenRepository refreshTokenRepository,
    IGoogleTokenValidator googleTokenValidator)
{
    public const string GoogleProvider = "Google";
    public const string UnlinkedAccountMessage =
        "Esta cuenta de Google no está vinculada. Iniciá sesión con tu usuario y contraseña y vinculala desde tu perfil.";

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

        if (!payload.EmailVerified)
        {
            throw new InvalidCredentialsException("El correo de Google no está verificado.");
        }

        // 2. Buscar únicamente por vínculo explícito mediante Google Subject (sub)
        var user = await userManager.FindByLoginAsync(GoogleProvider, payload.Subject);
        if (user == null)
        {
            throw new InvalidCredentialsException(UnlinkedAccountMessage);
        }

        // 3. Validar estado de cuenta (activo, no bloqueado, y email confirmado para PF)
        var roles = await AccountAccessValidator.EnsureAccountCanAuthenticateAsync(
            userManager,
            user,
            UnlinkedAccountMessage);

        // 4. Reiniciar contador de intentos fallidos
        await userManager.ResetAccessFailedCountAsync(user);

        // 5. Emitir JWT propio de ProTraining (con sub, role y pf_id internos)
        var generatedToken = jwtTokenGenerator.GenerateToken(user, roles);

        // 6. Generar y persistir refresh token de 30 días
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
                user.MustChangePassword,
                user.TelegramUsername
            )
        );
    }
}
