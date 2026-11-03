using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.GetPfContact;

public class GetPfContactUseCase(UserManager<User> userManager)
{
    // Devuelve el WhatsApp del PF, o null si el PF no existe o todavía no lo cargó.
    public async Task<string?> ExecuteAsync(Guid pfId)
    {
        var pf = await userManager.FindByIdAsync(pfId.ToString());
        return string.IsNullOrWhiteSpace(pf?.PhoneNumber) ? null : pf.PhoneNumber;
    }
}