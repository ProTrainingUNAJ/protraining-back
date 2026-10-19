using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class AthleteRecordRepository : IAthleteRecordRepository
{
    private readonly AppDbContext _context;

    public AthleteRecordRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<AthleteRecord?> GetActiveRecordAsync(Guid athleteId, Guid exerciseId, CancellationToken cancellationToken = default)
    {
        return await _context.AthleteRecords
            .FirstOrDefaultAsync(r => r.AthleteId == athleteId
                                   && r.ExerciseId == exerciseId
                                   && r.IsActive, cancellationToken);
    }

    public async Task<List<AthleteRecord>> ListAsync(Guid athleteId, bool currentOnly, CancellationToken cancellationToken = default)
    {
        return await _context.AthleteRecords
            .AsNoTracking()
            .Include(r => r.Exercise)
            .Where(r => r.AthleteId == athleteId && r.Source != RecordSource.NoData)
            .Where(r => !currentOnly || r.IsActive)
            .OrderBy(r => r.Exercise!.Name)
            .ThenByDescending(r => r.Date)
            .ToListAsync(cancellationToken);
    }

    public async Task<string?> GetExerciseNameAsync(Guid exerciseId, CancellationToken cancellationToken = default)
    {
        return await _context.Exercises
            .AsNoTracking()
            .Where(e => e.Id == exerciseId)
            .Select(e => e.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(AthleteRecord record, CancellationToken cancellationToken = default)
    {
        await _context.AthleteRecords.AddAsync(record, cancellationToken);
    }

    public Task UpdateAsync(AthleteRecord record, CancellationToken cancellationToken = default)
    {
        _context.AthleteRecords.Update(record);
        return Task.CompletedTask;
    }
}