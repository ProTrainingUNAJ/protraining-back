using Application.DTOs.Users;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.GetPfContact;

public class GetPfContactUseCase(UserManager<User> userManager)
{
    // Devuelve los canales de contacto del PF (WhatsApp y/o Telegram), o null si el PF no existe o no cargó ninguno.
    public async Task<PfContactDto?> ExecuteAsync(Guid pfId)
    {
        var pf = await userManager.FindByIdAsync(pfId.ToString());
        if (pf is null)
        {
            return null;
        }

        var phone = string.IsNullOrWhiteSpace(pf.PhoneNumber) ? null : pf.PhoneNumber;
        var telegram = string.IsNullOrWhiteSpace(pf.TelegramUsername) ? null : pf.TelegramUsername;

        if (phone is null && telegram is null)
        {
            return null;
        }

        return new PfContactDto(phone, telegram);
    }
}
