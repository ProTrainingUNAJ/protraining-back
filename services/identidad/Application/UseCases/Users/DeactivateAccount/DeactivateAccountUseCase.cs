using Application.DTOs.Users;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Users.DeactivateAccount;

/// <summary>
/// Caso de uso para la baja lógica de la cuenta del Preparador Físico confirmando su contraseña (KAN-601).
/// </summary>
public class DeactivateAccountUseCase(
    UserManager<User> userManager,
    IRefreshTokenRepository refreshTokenRepository)
{
    public async Task ExecuteAsync(
        Guid userId,
        DeactivateAccountRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new DomainException("La contraseña es obligatoria para confirmar la desactivación.");
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null || !user.IsActive)
        {
            throw new DomainException("Usuario no encontrado o ya inactivo.");
        }

        var isPasswordValid = await userManager.CheckPasswordAsync(user, request.Password);
        if (!isPasswordValid)
        {
            throw new InvalidCredentialsException("La contraseña ingresada es incorrecta.");
        }

        // Baja lógica: IsActive = false
        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new DomainException($"Error al desactivar la cuenta: {errors}");
        }

        // Revocar todos los refresh tokens activos para cerrar todas las sesiones
        await refreshTokenRepository.RevokeAllActiveByUserAsync(user.Id, cancellationToken);
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);
    }
}
