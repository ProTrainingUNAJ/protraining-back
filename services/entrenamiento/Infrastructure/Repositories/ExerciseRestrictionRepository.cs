using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ExerciseRestrictionRepository : IExerciseRestrictionRepository
{
    private readonly AppDbContext _context;

    public ExerciseRestrictionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<ExerciseRestriction>> GetRestrictionsByZonesAndExercisesAsync(
        IEnumerable<string> zones,
        IEnumerable<Guid> exerciseIds,
        CancellationToken cancellationToken = default)
    {
        var zonesList = zones.Select(z => z.Trim().ToLowerInvariant()).ToList();
        var exerciseIdsList = exerciseIds.ToList();

        if (zonesList.Count == 0 || exerciseIdsList.Count == 0)
            return [];

        return await _context.ExerciseRestrictions
            .AsNoTracking()
            .Include(r => r.Substitutes)
                .ThenInclude(s => s.SubstituteExercise)
            .Where(r => zonesList.Contains(r.Zone) && exerciseIdsList.Contains(r.ExerciseId))
            .ToListAsync(cancellationToken);
    }

    public async Task<ExerciseRestriction?> GetRestrictionByExerciseAndZoneAsync(
        Guid exerciseId,
        string zone,
        CancellationToken cancellationToken = default)
    {
        var normalizedZone = zone.Trim().ToLowerInvariant();

        return await _context.ExerciseRestrictions
            .AsNoTracking()
            .Include(r => r.Substitutes)
                .ThenInclude(s => s.SubstituteExercise)
            .FirstOrDefaultAsync(r => r.ExerciseId == exerciseId && r.Zone == normalizedZone, cancellationToken);
    }
}

