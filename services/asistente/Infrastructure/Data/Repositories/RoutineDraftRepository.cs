namespace Infrastructure.Data.Repositories;

using Application.Interfaces.Persistence;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Implementación con Entity Framework Core del repositorio para los borradores de rutinas (#088 · KAN-383).
/// Aplica automáticamente el filtro por PfId y DeletedAt configurado a nivel global en AppDbContext.
/// </summary>
public sealed class RoutineDraftRepository(AppDbContext dbContext) : IRoutineDraftRepository
{
    public async Task AddAsync(RoutineDraft routineDraft, CancellationToken cancellationToken = default)
    {
        await dbContext.RoutineDrafts.AddAsync(routineDraft, cancellationToken);
    }

    public async Task<RoutineDraft?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.RoutineDrafts
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<RoutineDraft>> GetByAthleteIdAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        return await dbContext.RoutineDrafts
            .Where(d => d.AthleteId == athleteId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
