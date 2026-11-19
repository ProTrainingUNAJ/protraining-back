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
    public async Task<PurgedAthleteDataDto> PurgeAthleteDataAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);

        int photosCount = await context.UserPhotos
            .Where(p => p.UserId == athleteId)
            .ExecuteDeleteAsync(cancellationToken);

        int refreshTokensCount = await context.RefreshTokens
            .Where(r => r.UserId == athleteId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.UserRoles
            .Where(ur => ur.UserId == athleteId)
            .ExecuteDeleteAsync(cancellationToken);

        // Seguridad: solo purga usuarios con rol de atleta (PfId asignado al PF dueño)
        int usersCount = await context.Users
            .Where(u => u.Id == athleteId && u.PfId != null)
            .ExecuteDeleteAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        int totalRecordsPurged = photosCount + refreshTokensCount + usersCount;

        return new PurgedAthleteDataDto(
            athleteId,
            usersCount,
            refreshTokensCount,
            photosCount,
            totalRecordsPurged);
    }
}
