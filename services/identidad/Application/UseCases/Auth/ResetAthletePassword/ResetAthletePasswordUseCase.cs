using Application.DTOs.Auth;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.ResetAthletePassword;

/// <summary>
/// Caso de uso que permite a un Preparador Físico restablecer la contraseña de un atleta a su cargo.
/// Genera una contraseña temporal segura (CSPRNG), almacena únicamente su hash, marca la cuenta
/// para cambio obligatorio (MustChangePassword = true) y revoca todos los refresh tokens activos del atleta.
/// </summary>
public class ResetAthletePasswordUseCase(
    UserManager<User> userManager,
    IRefreshTokenRepository refreshTokenRepository,
    ITemporaryPasswordGenerator passwordGenerator,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public async Task<ResetAthletePasswordResponseDto> ExecuteAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        // 1. Obtener el identificador del PF autenticado desde el JWT (sub / pf_id)
        var currentPfId = currentUser.UserId;
        if (currentPfId is null)
        {
            throw new UnauthorizedAccessException();
        }

        // 2. Buscar al atleta por su Id
        var athlete = await userManager.FindByIdAsync(athleteId.ToString());
        if (athlete is null || !athlete.IsActive)
        {
            throw new AthleteNotFoundException();
        }

        // 3. Aislamiento Multitenant: verificar que el atleta pertenezca al PF autenticado
        // Retorna la misma excepción 404 para no filtrar información entre preparadores físicos
        if (!athlete.PfId.HasValue || athlete.PfId.Value != currentPfId.Value)
        {
            throw new AthleteNotFoundException();
        }

        // 4. Validar que el usuario reseteado sea efectivamente un Atleta
        var isAthlete = await userManager.IsInRoleAsync(athlete, "Atleta");
        if (!isAthlete)
        {
            throw new AthleteNotFoundException();
        }

        // 5. Generar contraseña temporal criptográficamente segura (CSPRNG)
        var temporaryPassword = passwordGenerator.Generate(12);

        // 6. Almacenar únicamente su hash PBKDF2, nuevo SecurityStamp y marcar para cambio obligatorio
        athlete.PasswordHash = userManager.PasswordHasher.HashPassword(athlete, temporaryPassword);
        athlete.SecurityStamp = Guid.NewGuid().ToString();
        athlete.MustChangePassword = true;
        athlete.UpdatedAt = DateTime.UtcNow;

        // Ejecutar de forma atómica: actualización de usuario, reinicio de intentos fallidos y revocación de tokens
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var updateResult = await userManager.UpdateAsync(athlete);
            if (!updateResult.Succeeded)
            {
                var errors = string.Join("; ", updateResult.Errors.Select(e => e.Description));
                throw new DomainException($"No se pudo resetear la contraseña: {errors}");
            }

            // 7. Reiniciar contador de intentos fallidos
            await userManager.ResetAccessFailedCountAsync(athlete);

            // 8. Revocar todos los refresh tokens activos del atleta (cierre de todas sus sesiones)
            await refreshTokenRepository.RevokeAllActiveByUserAsync(athlete.Id, cancellationToken);
        }, cancellationToken);

        // 9. Retornar la respuesta con la contraseña temporal generada
        return new ResetAthletePasswordResponseDto(
            AthleteId: athlete.Id,
            TemporaryPassword: temporaryPassword,
            MustChangePassword: true);
    }
}
