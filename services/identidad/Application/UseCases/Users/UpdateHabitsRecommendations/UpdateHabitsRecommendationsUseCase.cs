using Application.DTOs.Auth;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Users.UpdateHabitsRecommendations;

public class UpdateHabitsRecommendationsUseCase(UserManager<User> userManager)
{
    public async Task<bool> ExecuteAsync(Guid userId, bool enabled)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        user.HabitsRecommendationsEnabled = enabled;
        user.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        return result.Succeeded;
    }
}