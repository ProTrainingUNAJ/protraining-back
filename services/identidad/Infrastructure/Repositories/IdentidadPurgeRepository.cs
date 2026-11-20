using Application.DTOs.Internal;
using Application.Interfaces.Persistence;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <summary>
/// Implementación de purga física de datos de un usuario/atleta en Identidad (#204 · KAN-499).
/// </summary>
public sealed class IdentidadPurgeRepository(AppDbContext context) : IIdentidadPurgeRepository
{
    public async Task<PurgedAthleteDataDto> PurgeAthleteDataAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        // Seguridad: solo purga usuarios con rol de atleta (PfId asignado al PF dueño).
        // Si no existe o es una cuenta de PF/Admin, no se tocan tablas dependientes.
        bool isAthlete = await context.Users
            .AnyAsync(u => u.Id == accountId && u.PfId != null, cancellationToken);

        if (!isAthlete)
        {
            return new PurgedAthleteDataDto(accountId, 0, 0, 0, 0, 0);
        }

        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);

        int photosCount = await context.UserPhotos
            .Where(p => p.UserId == accountId)
            .ExecuteDeleteAsync(cancellationToken);

        int refreshTokensCount = await context.RefreshTokens
            .Where(r => r.UserId == accountId)
            .ExecuteDeleteAsync(cancellationToken);

        int userRolesCount = await context.UserRoles
            .Where(ur => ur.UserId == accountId)
            .ExecuteDeleteAsync(cancellationToken);

        int usersCount = await context.Users
            .Where(u => u.Id == accountId && u.PfId != null)
            .ExecuteDeleteAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        int totalRecordsPurged = photosCount + refreshTokensCount + userRolesCount + usersCount;

        return new PurgedAthleteDataDto(
            accountId,
            usersCount,
            refreshTokensCount,
            photosCount,
            totalRecordsPurged,
            userRolesCount);
    }
}
