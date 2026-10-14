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