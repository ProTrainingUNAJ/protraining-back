using System.Net.Mail;
using Application.DTOs.Auth;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.ResendConfirmation;

/// <summary>
/// Caso de uso para reenviar el correo de confirmación de cuenta de forma anti-enumeración (KAN-601).
/// </summary>
public class ResendConfirmationUseCase(
    UserManager<User> userManager,
    IEmailSender emailSender)
{
    public const string DefaultResponse =
        "Si la cuenta existe y está pendiente de confirmación, te enviamos un nuevo correo.";

    public async Task<ResendConfirmationResponseDto> ExecuteAsync(
        ResendConfirmationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !MailAddress.TryCreate(request.Email.Trim(), out var parsedAddress))
        {
            throw new DomainException("Debe ingresar un correo electrónico válido.");
        }

        var cleanEmail = parsedAddress.Address.ToLowerInvariant();
        var user = await userManager.FindByEmailAsync(cleanEmail);

        if (user != null && user.IsActive && !user.EmailConfirmed)
        {
            try
            {
                var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                await emailSender.SendConfirmationEmailAsync(cleanEmail, user.Id, token, cancellationToken);
            }
            catch
            {
                // Mitigación anti-enumeración
            }
        }

        return new ResendConfirmationResponseDto(DefaultResponse);
    }
}
