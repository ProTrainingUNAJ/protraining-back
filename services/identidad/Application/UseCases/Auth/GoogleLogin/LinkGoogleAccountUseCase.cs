using Application.Common.Security;
using Application.DTOs.Auth;
using Application.Exceptions;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.GoogleLogin;

/// <summary>
/// Vincula explícitamente una cuenta de Google a la cuenta ProTraining autenticada,
/// exigiendo reautenticación reciente mediante contraseña actual y coincidencia de correo verificado (KAN-541).
/// </summary>
public class LinkGoogleAccountUseCase(
    UserManager<User> userManager,
    IGoogleTokenValidator googleTokenValidator)
{
    public const string GoogleProvider = "Google";

    public async Task<GoogleLinkStatusResponseDto> ExecuteAsync(
        Guid userId,
        LinkGoogleAccountRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            throw new DomainException("El token de Google es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            throw new InvalidCredentialsException("Necesitás confirmar tu identidad con tu contraseña actual para vincular Google.");
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        await AccountAccessValidator.EnsureAccountCanAuthenticateAsync(userManager, user);

        // 1. Reautenticación obligatoria con la contraseña actual del usuario
        var passwordValid = await userManager.CheckPasswordAsync(user!, request.CurrentPassword);
        if (!passwordValid)
        {
            await userManager.AccessFailedAsync(user!);
            throw new InvalidCredentialsException("Necesitás confirmar tu identidad con tu contraseña actual para vincular Google.");
        }

        await userManager.ResetAccessFailedCountAsync(user!);

        // 2. Validar criptográficamente el ID Token de Google
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

        // 3. Comparar el correo de Google con el correo de la cuenta interna
        if (!string.Equals(payload.Email.Trim(), user!.Email?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("El correo de Google no coincide con el de tu cuenta.");
        }

        // 4. Comprobar si ese Google Subject ya pertenece a otro usuario
        var existingOwner = await userManager.FindByLoginAsync(GoogleProvider, payload.Subject);
        if (existingOwner != null && existingOwner.Id != user.Id)
        {
            throw new PersistenceConflictException("Esta cuenta de Google ya está vinculada a otro usuario.");
        }

        // 5. Verificar los logins actuales del usuario (idempotencia si es el mismo sub; rechazo si es otro sub)
        var currentLogins = await userManager.GetLoginsAsync(user);
        var existingGoogleLogin = currentLogins.FirstOrDefault(l => l.LoginProvider == GoogleProvider);
        if (existingGoogleLogin != null)
        {
            if (existingGoogleLogin.ProviderKey == payload.Subject)
            {
                return new GoogleLinkStatusResponseDto(GoogleLinked: true);
            }

            throw new PersistenceConflictException("Tu cuenta ya está vinculada a otra cuenta de Google.");
        }

        // 6. Persistir vínculo en user_logins manejando condiciones de carrera por restricción única
        try
        {
            var addResult = await userManager.AddLoginAsync(
                user,
                new UserLoginInfo(GoogleProvider, payload.Subject, GoogleProvider));

            if (!addResult.Succeeded)
            {
                if (addResult.Errors.Any(e =>
                    e.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) ||
                    e.Description.Contains("already", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new PersistenceConflictException("Esta cuenta de Google ya está vinculada a otro usuario.");
                }

                throw new DomainException("No se pudo completar la vinculación. Intentá nuevamente.");
            }
        }
        catch (PersistenceConflictException)
        {
            throw;
        }
        catch (Exception ex) when (ex.InnerException?.Message.Contains("23505", StringComparison.OrdinalIgnoreCase) == true ||
                                   ex.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
        {
            throw new PersistenceConflictException("Esta cuenta de Google ya está vinculada a otro usuario.");
        }

        return new GoogleLinkStatusResponseDto(GoogleLinked: true);
    }
}
