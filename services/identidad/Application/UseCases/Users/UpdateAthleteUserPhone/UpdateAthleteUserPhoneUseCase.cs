using Application.DTOs.Users;
using Application.Exceptions;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Users.UpdateAthleteUserPhone;

public class UpdateAthleteUserPhoneUseCase(
    UserManager<User> userManager,
    ICurrentUser currentUser)
{
    public async Task ExecuteAsync(
        Guid athleteId,
        UpdateAthleteUserPhoneRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var currentPfId = currentUser.UserId;
        if (currentPfId is null)
        {
            throw new UnauthorizedAccessException();
        }

        var cleanPhone = request.PhoneNumber?.Trim();
        if (string.IsNullOrWhiteSpace(cleanPhone) || cleanPhone.Length > 50)
        {
            throw new DomainException("El teléfono no puede estar vacío ni superar los 50 caracteres.");
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

        athlete.PhoneNumber = cleanPhone;
        athlete.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(athlete);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new DomainException($"No se pudo actualizar el teléfono del atleta: {errors}");
        }
    }
}
