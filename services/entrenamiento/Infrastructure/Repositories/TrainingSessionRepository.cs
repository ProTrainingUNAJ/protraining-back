using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class TrainingSessionRepository : ITrainingSessionRepository
{
    private readonly AppDbContext _context;

    public TrainingSessionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<TrainingSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.TrainingSessions
            .Include(s => s.PrescribedExercises)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public Task UpdateAsync(TrainingSession session, CancellationToken cancellationToken = default)
    {
        _context.TrainingSessions.Update(session);
        return Task.CompletedTask;
    }
}
