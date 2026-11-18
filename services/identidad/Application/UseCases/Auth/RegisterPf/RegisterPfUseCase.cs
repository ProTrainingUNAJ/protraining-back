using System.Net.Mail;
using Application.DTOs.Auth;
using Application.Exceptions;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Services;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.RegisterPf;

/// <summary>
/// Caso de uso para el registro independiente de un Preparador Físico (KAN-601).
/// </summary>
public class RegisterPfUseCase(
    UserManager<User> userManager,
    IEmailSender emailSender)
{
    public async Task<RegisterPfResponseDto> ExecuteAsync(
        RegisterPfRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
            throw new DomainException("El nombre completo es obligatorio.");

        if (string.IsNullOrWhiteSpace(request.Email) || !MailAddress.TryCreate(request.Email.Trim(), out var parsedAddress))
            throw new DomainException("Debe ingresar un correo electrónico válido.");

        if (string.IsNullOrWhiteSpace(request.Password))
            throw new DomainException("La contraseña es obligatoria.");

        var cleanEmail = parsedAddress.Address.ToLowerInvariant();

        // 1. Si el email ya existe (sea PF, atleta o tutor): 409 con mensaje neutro
        var existingUser = await userManager.FindByEmailAsync(cleanEmail);
        if (existingUser != null)
        {
            throw new EmailAlreadyExistsException();
        }

        // 2. Normalizar teléfono si se proporcionó
        string? normalizedPhone = null;
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            var (isValid, normalized, error) = WhatsAppPhoneNormalizer.ValidateAndNormalize(request.Phone);
            if (!isValid)
            {
                throw new DomainException(error ?? "El número de teléfono no es válido.");
            }
            normalizedPhone = normalized;
        }

        // 3. Crear el usuario con EmailConfirmed = false y PfId = null (es su propio dueño)
        var user = new User
        {
            UserName = cleanEmail,
            Email = cleanEmail,
            FullName = request.FullName.Trim(),
            PhoneNumber = normalizedPhone,
            PfId = null,
            EmailConfirmed = false,
            IsActive = true
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
            throw new DomainException($"No se pudo registrar el usuario: {errors}");
        }

        // 4. Asignar rol PreparadorFisico
        var roleResult = await userManager.AddToRoleAsync(user, "PreparadorFisico");
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            var errors = string.Join("; ", roleResult.Errors.Select(e => e.Description));
            throw new DomainException($"No se pudo asignar el rol Preparador Físico: {errors}");
        }

        // 5. Generar token de confirmación de email y enviar correo
        try
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            await emailSender.SendConfirmationEmailAsync(cleanEmail, user.Id, token, cancellationToken);
        }
        catch
        {
            // Si el envío de email falla, no abortamos la creación para permitir reintento con resend-confirmation
        }

        return new RegisterPfResponseDto(
            Id: user.Id,
            Email: user.Email!,
            FullName: user.FullName,
            Message: $"Te mandamos un mail a {user.Email}. Confirmalo para entrar."
        );
    }
}
