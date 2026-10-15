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

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.TrainingPlans.AnyAsync(p => p.Id == id, cancellationToken);
    }

    public async Task AddAssignmentAsync(PlanAssignment assignment, CancellationToken cancellationToken = default)
    {
        await _context.PlanAssignments.AddAsync(assignment, cancellationToken);
    }
}
