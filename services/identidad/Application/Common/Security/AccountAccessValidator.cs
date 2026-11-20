using Application.Exceptions;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Application.Common.Security;

/// <summary>
/// Validaciones compartidas de estado de cuenta y elegibilidad para autenticación/vinculación.
/// </summary>
public static class AccountAccessValidator
{
    public static async Task<IList<string>> EnsureAccountCanAuthenticateAsync(
        UserManager<User> userManager,
        User? user,
        string? inactiveMessage = null)
    {
        if (user == null || !user.IsActive)
        {
            throw new InvalidCredentialsException(inactiveMessage ?? "Credenciales inválidas.");
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            throw new InvalidCredentialsException("La cuenta se encuentra bloqueada.");
        }

        var roles = await userManager.GetRolesAsync(user);
        if (roles.Contains("PreparadorFisico") && !user.EmailConfirmed)
        {
            throw new EmailNotConfirmedException();
        }

        return roles;
    }
}
