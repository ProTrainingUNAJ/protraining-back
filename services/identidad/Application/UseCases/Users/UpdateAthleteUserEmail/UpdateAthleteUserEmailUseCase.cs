using Application.DTOs.Users;
using Application.Exceptions;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Users.UpdateAthleteUserEmail;

public class UpdateAthleteUserEmailUseCase(
    UserManager<User> userManager,
    ICurrentUser currentUser)
{
    public async Task ExecuteAsync(
        Guid athleteId,
        UpdateAthleteUserEmailRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var currentPfId = currentUser.UserId;
        if (currentPfId is null)
        {
            throw new UnauthorizedAccessException();
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new DomainException("El correo electrónico no puede estar vacío.");
        }

        var cleanEmail = request.Email.Trim().ToLowerInvariant();
        if (!cleanEmail.Contains('@') || !cleanEmail.Contains('.'))
        {
            throw new DomainException("El formato del correo electrónico no es válido.");
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

        var existingUser = await userManager.FindByEmailAsync(cleanEmail);
        if (existingUser is not null && existingUser.Id != athlete.Id)
        {
            throw new DomainException("El correo electrónico ya se encuentra registrado.");
        }

        athlete.Email = cleanEmail;
        athlete.NormalizedEmail = cleanEmail.ToUpperInvariant();
        athlete.UserName = cleanEmail;
        athlete.NormalizedUserName = cleanEmail.ToUpperInvariant();
        athlete.SecurityStamp = Guid.NewGuid().ToString();
        athlete.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(athlete);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new DomainException($"No se pudo actualizar el correo electrónico del atleta: {errors}");
        }
    }
}
