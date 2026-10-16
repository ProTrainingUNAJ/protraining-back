using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class TrainingPlanRepository : ITrainingPlanRepository
{
    private readonly AppDbContext _context;

    public TrainingPlanRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<TrainingPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.TrainingPlans
            .Include(p => p.Weeks)
            .Include(p => p.Assignments)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<TrainingPlan?> GetWithDetailsByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.TrainingPlans
            .Include(p => p.Weeks.OrderBy(w => w.WeekNumber))
                .ThenInclude(w => w.Sessions.OrderBy(s => s.Order))
                    .ThenInclude(s => s.PrescribedExercises.OrderBy(e => e.Order))
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<List<TrainingPlan>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _context.TrainingPlans
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.TrainingPlans.AnyAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<bool> AllExercisesExistAsync(IEnumerable<Guid> exerciseIds, CancellationToken cancellationToken = default)
    {
        var distinctIds = exerciseIds.Distinct().ToList();
        if (distinctIds.Count == 0)
            return true;

        var count = await _context.Exercises
            .CountAsync(e => distinctIds.Contains(e.Id), cancellationToken);

        return count == distinctIds.Count;
    }

    public async Task AddAsync(TrainingPlan plan, CancellationToken cancellationToken = default)
    {
        await _context.TrainingPlans.AddAsync(plan, cancellationToken);
    }

    public async Task AddAssignmentAsync(PlanAssignment assignment, CancellationToken cancellationToken = default)
    {
        await _context.PlanAssignments.AddAsync(assignment, cancellationToken);
    }
}
