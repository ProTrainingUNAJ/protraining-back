using Application.DTOs.Users;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Services;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Users.UpdateProfile;

/// <summary>
/// Caso de uso para que el Preparador Físico actualice su nombre y teléfono (KAN-601).
/// </summary>
public class UpdateProfileUseCase(UserManager<User> userManager)
{
    public async Task<ProfileResponseDto> ExecuteAsync(
        Guid userId,
        UpdateProfileRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            throw new DomainException("El nombre completo es obligatorio.");
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null || !user.IsActive)
        {
            throw new DomainException("Usuario no encontrado.");
        }

        string? normalizedPhone = user.PhoneNumber;
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            var (isValid, normalized, error) = WhatsAppPhoneNormalizer.ValidateAndNormalize(request.Phone);
            if (!isValid)
            {
                throw new DomainException(error ?? "El número de teléfono no es válido.");
            }
            normalizedPhone = normalized;
        }
        else if (request.Phone is not null && string.IsNullOrWhiteSpace(request.Phone))
        {
            normalizedPhone = null;
        }

        string? normalizedTelegram = user.TelegramUsername;
        if (!string.IsNullOrWhiteSpace(request.TelegramUsername))
        {
            var (isValidTelegram, normalizedTg, telegramError) = TelegramUsernameNormalizer.ValidateAndNormalize(request.TelegramUsername);
            if (!isValidTelegram)
            {
                throw new DomainException(telegramError ?? "El usuario de Telegram no es válido.");
            }
            normalizedTelegram = normalizedTg;
        }
        else if (request.TelegramUsername is not null && string.IsNullOrWhiteSpace(request.TelegramUsername))
        {
            normalizedTelegram = null;
        }

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = normalizedPhone;
        user.TelegramUsername = normalizedTelegram;
        user.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new DomainException($"Error al actualizar el perfil: {errors}");
        }

        return new ProfileResponseDto(
            Id: user.Id,
            Email: user.Email!,
            FullName: user.FullName,
            PhoneNumber: user.PhoneNumber,
            TelegramUsername: user.TelegramUsername
        );
    }
}
