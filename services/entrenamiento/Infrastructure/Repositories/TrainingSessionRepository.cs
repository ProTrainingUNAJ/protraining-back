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

    public async Task<TrainingSession?> GetByAthleteIdAsync(Guid athleteId, IReadOnlyList<Guid> categoryIds, DateOnly date, CancellationToken cancellationToken = default)
    {
        // 1. Obtenemos las asignaciones del atleta o sus categorías, incluyendo el plan completo con semanas y sesiones
        var assignments = await _context.PlanAssignments
            .Include(a => a.Plan)
                .ThenInclude(p => p.Weeks)
                    .ThenInclude(w => w.Sessions)
                        .ThenInclude(s => s.PrescribedExercises.OrderBy(pe => pe.Order))
                            .ThenInclude(pe => pe.Exercise)
            .Where(a => a.AthleteId == athleteId ||
                        (a.CategoryId.HasValue && categoryIds.Contains(a.CategoryId.Value)))
            .OrderByDescending(a => a.AthleteId.HasValue) // Prioridad: plan asignado directo al atleta
            .ThenByDescending(a => a.StartDate)          // Luego el más reciente
            .ToListAsync(cancellationToken);

        // 2. La primera asignación (en ese orden) que tenga sesión en la fecha pedida
        return assignments
            .Select(assignment => assignment.SessionOn(date))
            .FirstOrDefault(session => session != null);
    }

    public async Task<List<PlanAssignment>> ListAssignmentsWithPlansAsync(CancellationToken cancellationToken = default)
    {
        // Todas las asignaciones del PF del token (filtro global), con el plan, sus semanas y sesiones
        return await _context.PlanAssignments
            .AsNoTracking()
            .Include(a => a.Plan)
                .ThenInclude(p => p.Weeks)
                    .ThenInclude(w => w.Sessions)
                        .ThenInclude(s => s.PrescribedExercises)
            .OrderBy(a => a.StartDate)
            .ToListAsync(cancellationToken);
    }

    public Task UpdateAsync(TrainingSession session, CancellationToken cancellationToken = default)
    {
        _context.TrainingSessions.Update(session);
        return Task.CompletedTask;
    }

    public async Task<List<PlanAssignment>> ListAssignmentsWithPlansByPfIdAsync(Guid pfId, CancellationToken cancellationToken = default)
    {
        // Apaga el filtro global porque el worker no tiene usuario logueado, y filtra explícitamente por el pfId del parámetro
        return await _context.PlanAssignments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(a => a.Plan)
                .ThenInclude(p => p.Weeks)
                    .ThenInclude(w => w.Sessions)
            .Where(a => a.PfId == pfId)
            .OrderBy(a => a.StartDate)
            .ToListAsync(cancellationToken);
    }
}