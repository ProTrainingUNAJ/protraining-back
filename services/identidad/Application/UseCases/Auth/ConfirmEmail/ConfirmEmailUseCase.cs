using Application.DTOs.Auth;
using Application.Exceptions;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.ConfirmEmail;

/// <summary>
/// Caso de uso que valida el token de confirmación y activa el correo de la cuenta (KAN-601).
/// </summary>
public class ConfirmEmailUseCase(UserManager<User> userManager)
{
    public async Task<ConfirmEmailResponseDto> ExecuteAsync(
        ConfirmEmailRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.UserId == Guid.Empty)
            throw new DomainException("El identificador de usuario es obligatorio.");

        if (string.IsNullOrWhiteSpace(request.Token))
            throw new InvalidConfirmationTokenException("El token de confirmación es obligatorio.");

        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user == null || !user.IsActive)
        {
            throw new InvalidConfirmationTokenException("El link de confirmación es inválido o ha expirado.");
        }

        if (user.EmailConfirmed)
        {
            return new ConfirmEmailResponseDto("¡Listo! Tu correo ya se encontraba confirmado. Ya podés entrar.");
        }

        var result = await userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
        {
            throw new InvalidConfirmationTokenException("El link de confirmación es inválido o ha expirado.");
        }

        return new ConfirmEmailResponseDto("¡Listo! Tu correo fue confirmado correctamente. Ya podés entrar.");
    }
}
