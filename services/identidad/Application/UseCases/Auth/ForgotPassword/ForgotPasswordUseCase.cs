using System.Net.Mail;
using Application.DTOs.Auth;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.ForgotPassword;

/// <summary>
/// Caso de uso que procesa la solicitud de olvido de contraseña (#KAN-540).
/// Genera un token firmado con vigencia de 30 minutos y solicita el despacho de correo,
/// respondiendo con el mismo mensaje genérico HTTP 202 tanto si la cuenta existe como si no.
/// </summary>
public class ForgotPasswordUseCase(
    UserManager<User> userManager,
    IEmailSender emailSender)
{
    public const string DefaultSuccessMessage =
        "Si el correo está registrado, recibirás un enlace para recuperar tu contraseña.";

    public async Task<ForgotPasswordResponseDto> ExecuteAsync(
        ForgotPasswordRequestDto request,
        CancellationToken cancellationToken = default)
    {
        // 1. Validar formato de la solicitud
        if (string.IsNullOrWhiteSpace(request.Email) || !MailAddress.TryCreate(request.Email.Trim(), out var parsedAddress))
        {
            throw new DomainException("Debe ingresar un correo electrónico válido.");
        }

        var normalizedEmail = parsedAddress.Address;

        // 2. Buscar al usuario mediante Identity
        var user = await userManager.FindByEmailAsync(normalizedEmail);

        // 3. Si existe una cuenta elegible y activa, generar token y despachar correo
        if (user != null && user.IsActive)
        {
            try
            {
                // Generar token con DataProtectionTokenProvider de ASP.NET Core Identity
                var token = await userManager.GeneratePasswordResetTokenAsync(user);

                await emailSender.SendPasswordResetEmailAsync(
                    user.Email ?? normalizedEmail,
                    token,
                    cancellationToken);
            }
            catch
            {
                // Mitigación anti-enumeración: si el envío de correo falla, no propagar el error al cliente
            }
        }

        // 4. Retornar siempre la misma respuesta genérica (HTTP 202 Accepted)
        return new ForgotPasswordResponseDto(DefaultSuccessMessage);
    }
}
