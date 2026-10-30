using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class AttendanceRepository(AppDbContext context) : IAttendanceRepository
{
    public async Task<Attendance?> GetByAthleteAndDateAsync(Guid athleteId, DateTime date, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.Attendances
                .FirstOrDefaultAsync(
                    attendance => attendance.AthleteId == athleteId && attendance.Date == date,
                    cancellationToken));

    public async Task<IReadOnlyList<Attendance>> GetByAthletesAndDateAsync(IReadOnlyCollection<Guid> athleteIds, DateTime date, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.Attendances
                .AsNoTracking()
                .Where(attendance => athleteIds.Contains(attendance.AthleteId) && attendance.Date == date)
                .ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<Attendance>> GetByDateAsync(DateTime date, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.Attendances
                .AsNoTracking()
                .Where(attendance => attendance.Date == date)
                .ToListAsync(cancellationToken));

    public Task AddAsync(Attendance attendance, CancellationToken cancellationToken = default)
    {
        context.Attendances.Add(attendance);
        return Task.CompletedTask;
    }
}