using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Users.GetPfHabitsRecommendations;

public class GetPfHabitsRecommendationsUseCase(UserManager<User> userManager)
{
    // Devuelve la preferencia del PF, o null si el PF no existe.
    public async Task<bool?> ExecuteAsync(Guid pfId)
    {
        var pf = await userManager.FindByIdAsync(pfId.ToString());
        if (pf is null)
        {
            return null;
        }

        return pf.HabitsRecommendationsEnabled;
    }
}