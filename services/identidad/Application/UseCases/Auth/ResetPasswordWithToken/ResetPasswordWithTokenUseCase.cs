using Application.DTOs.Auth;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.ResetPasswordWithToken;

/// <summary>
/// Caso de uso que valida el token recibido por correo, aplica la nueva contraseña mediante ASP.NET Core Identity,
/// actualiza el SecurityStamp y revoca atómicamente todos los refresh tokens de la cuenta (#KAN-540).
/// </summary>
public class ResetPasswordWithTokenUseCase(
    UserManager<User> userManager,
    IRefreshTokenRepository refreshTokenRepository,
    IUnitOfWork unitOfWork)
{
    private const string InvalidOrExpiredTokenMessage =
        "El enlace de recuperación es inválido o ha vencido. Solicitá uno nuevo.";

    public async Task<ResetPasswordWithTokenResponseDto> ExecuteAsync(
        ResetPasswordWithTokenRequestDto request,
        CancellationToken cancellationToken = default)
    {
        // 1. Validaciones básicas de presencia
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new DomainException("El correo electrónico es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            throw new DomainException("El token de recuperación es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            throw new DomainException("La nueva contraseña no puede estar vacía.");
        }

        // 2. Buscar al usuario
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user == null || !user.IsActive)
        {
            throw new DomainException(InvalidOrExpiredTokenMessage);
        }

        // 3. Restablecer la contraseña mediante ASP.NET Core Identity
        // Valida la firma del token, la política unificada de contraseñas y muta automáticamente el SecurityStamp
        var resetResult = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!resetResult.Succeeded)
        {
            // Si el fallo es por complejidad de la contraseña, mostrar el detalle en español devuelto por IdentityErrorDescriber
            var passwordErrors = resetResult.Errors
                .Where(e => e.Code.StartsWith("Password", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Description)
                .ToList();

            if (passwordErrors.Count > 0)
            {
                throw new DomainException($"No se pudo restablecer la contraseña: {string.Join("; ", passwordErrors)}");
            }

            // En cualquier otro caso (token alterado, expirado o de otro usuario), mostrar mensaje claro
            throw new DomainException(InvalidOrExpiredTokenMessage);
        }

        // 4. Coordinación atómica mediante IUnitOfWork: desbloqueo, desactivación de MustChangePassword y revocación de tokens
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // Reiniciar intentos fallidos de autenticación
            await userManager.ResetAccessFailedCountAsync(user);

            // Si la cuenta requería cambio obligatorio previo (por KAN-539), liberarla
            if (user.MustChangePassword)
            {
                user.MustChangePassword = false;
                user.UpdatedAt = DateTime.UtcNow;
                await userManager.UpdateAsync(user);
            }

            // Revocar todos los refresh tokens activos (cierra sesiones en todos los dispositivos)
            await refreshTokenRepository.RevokeAllActiveByUserAsync(user.Id, cancellationToken);
        }, cancellationToken);

        return new ResetPasswordWithTokenResponseDto("Contraseña actualizada exitosamente.");
    }
}
