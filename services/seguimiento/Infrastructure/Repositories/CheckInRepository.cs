using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

// Acceso a la tabla local de check-ins: la única fuente de datos del semáforo (KAN-364).
public sealed class CheckInRepository(AppDbContext context) : ICheckInRepository
{
    public Task<CheckIn?> GetByAthleteAndDateAsync(Guid athleteId, DateTime date, CancellationToken cancellationToken = default) =>
        context.CheckIns
            .FirstOrDefaultAsync(checkIn => checkIn.AthleteId == athleteId && checkIn.Date == date, cancellationToken);

    public async Task AddAsync(CheckIn checkIn, CancellationToken cancellationToken = default)
    {
        await context.CheckIns.AddAsync(checkIn, cancellationToken);
    }
}
