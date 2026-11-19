using Application.Common.Security;
using Application.DTOs.Auth;
using Application.Exceptions;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.GoogleLogin;

/// <summary>
/// Desvincula de forma segura la cuenta de Google del usuario autenticado,
/// exigiendo reautenticación con contraseña actual y garantizando que conserve su acceso tradicional (KAN-541).
/// </summary>
public class UnlinkGoogleAccountUseCase(UserManager<User> userManager)
{
    public const string GoogleProvider = "Google";

    public async Task<GoogleLinkStatusResponseDto> ExecuteAsync(
        Guid userId,
        UnlinkGoogleAccountRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            throw new InvalidCredentialsException("Necesitás confirmar tu identidad con tu contraseña actual para desvincular Google.");
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        await AccountAccessValidator.EnsureAccountCanAuthenticateAsync(userManager, user);

        var hasPassword = await userManager.HasPasswordAsync(user!);
        if (!hasPassword)
        {
            throw new DomainException("No podés desvincular Google porque es tu único método de acceso.");
        }

        var passwordValid = await userManager.CheckPasswordAsync(user!, request.CurrentPassword);
        if (!passwordValid)
        {
            await userManager.AccessFailedAsync(user!);
            throw new InvalidCredentialsException("Necesitás confirmar tu identidad con tu contraseña actual para desvincular Google.");
        }

        await userManager.ResetAccessFailedCountAsync(user!);

        var logins = await userManager.GetLoginsAsync(user!);
        var googleLogin = logins.FirstOrDefault(l => l.LoginProvider == GoogleProvider);
        if (googleLogin != null)
        {
            var removeResult = await userManager.RemoveLoginAsync(user!, GoogleProvider, googleLogin.ProviderKey);
            if (!removeResult.Succeeded)
            {
                throw new DomainException("No se pudo desvincular la cuenta de Google.");
            }
        }

        return new GoogleLinkStatusResponseDto(GoogleLinked: false);
    }
}
