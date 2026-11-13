using Application.DTOs.Auth;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.ChangePassword;

/// <summary>
/// Caso de uso que valida la contraseña actual, actualiza la contraseña con reglas de seguridad
/// y revoca todos los refresh tokens activos del usuario.
/// </summary>
public class ChangePasswordUseCase(
    UserManager<User> userManager,
    IRefreshTokenRepository refreshTokenRepository)
{
    public async Task ExecuteAsync(Guid userId, ChangePasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        // 1. Obtener al usuario autenticado
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null || !user.IsActive)
        {
            throw new UnauthorizedAccessException();
        }

        // 2. Validar que la contraseña actual fue enviada
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            throw new DomainException("La contraseña actual es incorrecta.");
        }

        // 3. Verificar que la contraseña actual sea la correcta
        var isCurrentPasswordValid = await userManager.CheckPasswordAsync(user, request.CurrentPassword);
        if (!isCurrentPasswordValid)
        {
            await userManager.AccessFailedAsync(user);
            throw new DomainException("La contraseña actual es incorrecta.");
        }

        // 4. Validar que la nueva contraseña no esté vacía
        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            throw new DomainException("La nueva contraseña no puede estar vacía.");
        }

        // 5. Validar que la nueva contraseña no sea idéntica a la actual
        if (request.NewPassword == request.CurrentPassword)
        {
            throw new DomainException("La nueva contraseña no puede ser igual a la actual.");
        }

        // 6. Actualizar la contraseña del usuario (valida reglas nativas de Identity: mín 8 chars y dígito obligatorio)
        var changeResult = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changeResult.Succeeded)
        {
            var errors = string.Join("; ", changeResult.Errors.Select(e => e.Description));
            throw new DomainException($"No se pudo cambiar la contraseña: {errors}");
        }

        // 8. Reiniciar contador de intentos fallidos
        await userManager.ResetAccessFailedCountAsync(user);

        // 9. Si el usuario debía cambiar su contraseña por reseteo previo (KAN-539), desactivar flag
        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            user.UpdatedAt = DateTime.UtcNow;
            await userManager.UpdateAsync(user);
        }

        // 10. Revocar todos los refresh tokens activos del usuario en todos sus dispositivos
        await refreshTokenRepository.RevokeAllActiveByUserAsync(user.Id, cancellationToken);
    }
}

