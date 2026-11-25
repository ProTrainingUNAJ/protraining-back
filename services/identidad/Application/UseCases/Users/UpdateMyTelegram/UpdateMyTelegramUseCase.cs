using Application.DTOs.Users;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Services;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Users.UpdateMyTelegram;

/// <summary>
/// Permite al usuario autenticado (PF o Atleta) actualizar exclusivamente su propio usuario de Telegram (KAN-503).
/// Para atletas, sincroniza primero con Estructura antes de confirmar el cambio en Identidad para evitar
/// informar éxito si falla la propagación hacia el listado que consulta el PF.
/// </summary>
public sealed class UpdateMyTelegramUseCase(
    UserManager<User> userManager,
    IStructureClient structureClient)
{
    public async Task<ProfileResponseDto> ExecuteAsync(
        Guid userId,
        UpdateTelegramRequestDto request,
        string? bearerToken,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
        {
            throw new DomainException("Usuario no encontrado.");
        }

        string? normalizedTelegram = null;
        if (!string.IsNullOrWhiteSpace(request.TelegramUsername))
        {
            var (isValidTelegram, normalizedTg, telegramError) =
                TelegramUsernameNormalizer.ValidateAndNormalize(request.TelegramUsername);
            if (!isValidTelegram)
            {
                throw new DomainException(telegramError ?? "El usuario de Telegram no es válido.");
            }
            normalizedTelegram = normalizedTg;
        }

        if (await userManager.IsInRoleAsync(user, "Atleta"))
        {
            await structureClient.UpdateMyAthleteTelegramAsync(normalizedTelegram, bearerToken, cancellationToken);
        }

        user.TelegramUsername = normalizedTelegram;
        user.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new DomainException($"Error al actualizar el usuario de Telegram: {errors}");
        }

        return new ProfileResponseDto(
            Id: user.Id,
            Email: user.Email!,
            FullName: user.FullName,
            PhoneNumber: user.PhoneNumber,
            TelegramUsername: user.TelegramUsername);
    }
}
