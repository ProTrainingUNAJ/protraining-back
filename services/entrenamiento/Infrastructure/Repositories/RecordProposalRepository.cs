using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class RecordProposalRepository : IRecordProposalRepository
{
    private readonly AppDbContext _context;

    public RecordProposalRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<RecordProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.RecordProposals
            .Include(p => p.Exercise)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<List<RecordProposal>> ListAsync(RecordProposalStatus? status, CancellationToken cancellationToken = default)
    {
        var query = _context.RecordProposals
            .AsNoTracking()
            .Include(p => p.Exercise)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<RecordProposal?> GetPendingAsync(Guid athleteId, Guid exerciseId, CancellationToken cancellationToken = default)
    {
        return await _context.RecordProposals
            .FirstOrDefaultAsync(p => p.AthleteId == athleteId
                                   && p.ExerciseId == exerciseId
                                   && p.Status == RecordProposalStatus.Pending, cancellationToken);
    }

    public async Task<List<RecordProposal>> ListPendingByAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        return await _context.RecordProposals
            .AsNoTracking()
            .Include(p => p.Exercise)
            .Where(p => p.AthleteId == athleteId && p.Status == RecordProposalStatus.Pending)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(RecordProposal proposal, CancellationToken cancellationToken = default)
    {
        await _context.RecordProposals.AddAsync(proposal, cancellationToken);
    }

    public Task UpdateAsync(RecordProposal proposal, CancellationToken cancellationToken = default)
    {
        _context.RecordProposals.Update(proposal);
        return Task.CompletedTask;
    }
}
