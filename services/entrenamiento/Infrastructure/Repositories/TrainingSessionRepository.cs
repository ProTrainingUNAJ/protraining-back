using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

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

    public async Task<TrainingSession?> GetTodaySessionByAthleteIdAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        var todayDayOfWeek = DateTime.UtcNow.DayOfWeek;

        // Intenta traer la sesión planificada para el día de hoy
        var session = await _context.TrainingSessions
            .Include(s => s.PrescribedExercises.OrderBy(pe => pe.Order))
                .ThenInclude(pe => pe.Exercise)
            .Include(s => s.Week)
                .ThenInclude(w => w.TrainingPlan)
                    .ThenInclude(p => p.Assignments)
            .Where(s => s.Week.TrainingPlan.Assignments.Any(a => a.AthleteId == athleteId) && s.DayOfWeek == todayDayOfWeek)
            .FirstOrDefaultAsync(cancellationToken);

        // Si no tiene asignada ninguna sesión para hoy, toma la primera sesión activa del plan
        session ??= await _context.TrainingSessions
            .Include(s => s.PrescribedExercises.OrderBy(pe => pe.Order))
                .ThenInclude(pe => pe.Exercise)
            .Include(s => s.Week)
                .ThenInclude(w => w.TrainingPlan)
                    .ThenInclude(p => p.Assignments)
            .Where(s => s.Week.TrainingPlan.Assignments.Any(a => a.AthleteId == athleteId))
            .OrderBy(s => s.Week.WeekNumber)
            .ThenBy(s => s.Order)
            .FirstOrDefaultAsync(cancellationToken);

        return session;
    }

    public Task UpdateAsync(TrainingSession session, CancellationToken cancellationToken = default)
    {
        _context.TrainingSessions.Update(session);
        return Task.CompletedTask;
    }
}
