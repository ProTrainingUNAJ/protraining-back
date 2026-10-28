using Application.DTOs.Auth;
using Domain.Entities;
using Domain.Services;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.UpdatePhone;

public class UpdateWhatsAppPhoneUseCase(UserManager<User> userManager)
{
    public async Task<(bool Success, string? NormalizedPhone, string? ErrorMessage)> ExecuteAsync(Guid userId, UpdatePhoneRequestDto request)
    {
        var (isValid, normalized, errorMessage) = WhatsAppPhoneNormalizer.ValidateAndNormalize(request.PhoneNumber);
        if (!isValid)
        {
            return (false, null, errorMessage);
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return (false, null, "Usuario no encontrado.");
        }

        user.PhoneNumber = normalized;
        user.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return (false, null, "Error al actualizar el número de teléfono.");
        }

        return (true, normalized, null);
    }
}
