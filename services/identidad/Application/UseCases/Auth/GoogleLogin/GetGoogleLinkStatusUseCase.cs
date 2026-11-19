using Application.Common.Security;
using Application.DTOs.Auth;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.GoogleLogin;

/// <summary>
/// Consulta si el usuario autenticado posee una cuenta de Google vinculada, sin exponer el Google Subject (KAN-541).
/// </summary>
public class GetGoogleLinkStatusUseCase(UserManager<User> userManager)
{
    public const string GoogleProvider = "Google";

    public async Task<GoogleLinkStatusResponseDto> ExecuteAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        await AccountAccessValidator.EnsureAccountCanAuthenticateAsync(userManager, user);

        var logins = await userManager.GetLoginsAsync(user!);
        var isLinked = logins.Any(l => l.LoginProvider == GoogleProvider);

        return new GoogleLinkStatusResponseDto(GoogleLinked: isLinked);
    }
}
