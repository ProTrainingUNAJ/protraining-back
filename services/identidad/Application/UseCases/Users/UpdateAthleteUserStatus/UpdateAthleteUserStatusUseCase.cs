using Application.DTOs.Users;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Users.UpdateAthleteUserStatus;

public class UpdateAthleteUserStatusUseCase(
    UserManager<User> userManager,
    IRefreshTokenRepository refreshTokenRepository,
    ICurrentUser currentUser)
{
    public async Task ExecuteAsync(
        Guid athleteId,
        UpdateAthleteUserStatusRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var currentPfId = currentUser.UserId;
        if (currentPfId is null)
        {
            throw new UnauthorizedAccessException();
        }

        var athlete = await userManager.FindByIdAsync(athleteId.ToString());
        if (athlete is null)
        {
            throw new AthleteNotFoundException();
        }

        if (!athlete.PfId.HasValue || athlete.PfId.Value != currentPfId.Value)
        {
            throw new AthleteNotFoundException();
        }

        athlete.IsActive = request.IsActive;
        athlete.UpdatedAt = DateTime.UtcNow;

        if (!request.IsActive)
        {
            athlete.SecurityStamp = Guid.NewGuid().ToString();
            await refreshTokenRepository.RevokeAllActiveByUserAsync(athlete.Id, cancellationToken);
        }
        else
        {
            // Al reactivar al atleta, desbloquear cualquier lockout previo y reiniciar intentos fallidos
            await userManager.SetLockoutEndDateAsync(athlete, null);
            await userManager.ResetAccessFailedCountAsync(athlete);
        }

        var result = await userManager.UpdateAsync(athlete);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new DomainException($"No se pudo actualizar el estado del atleta: {errors}");
        }
    }
}
