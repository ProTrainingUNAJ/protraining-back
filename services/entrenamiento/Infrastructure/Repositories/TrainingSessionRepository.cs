using Application.Interfaces.Persistence;
using Domain.Common;
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

    public async Task<TrainingSession?> GetByAthleteIdAsync(Guid athleteId, IReadOnlyList<Guid> categoryIds, CancellationToken cancellationToken = default)
    {
        var today = ArgentinaTime.Today;
        var todayDayOfWeek = ArgentinaTime.DayOfWeek;

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

        // Evaluar cada asignación para ver cuál corresponde al día de hoy
        foreach (var assignment in assignments)
        {
            if (today < assignment.StartDate)
                continue;

            int daysPassed = today.DayNumber - assignment.StartDate.DayNumber;

            int weekNumber = (daysPassed / 7) + 1;

            var week = assignment.Plan.Weeks
                .FirstOrDefault(w => w.WeekNumber == weekNumber);

            if (week == null)
                continue;

            var todaySession = week.Sessions
                .FirstOrDefault(s => s.DayOfWeek == todayDayOfWeek);

            if (todaySession != null)
                return todaySession;
        }

        return null;
    }

    public Task UpdateAsync(TrainingSession session, CancellationToken cancellationToken = default)
    {
        _context.TrainingSessions.Update(session);
        return Task.CompletedTask;
    }
}