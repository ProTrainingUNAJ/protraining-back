using Application.DTOs.DailyLoad;
using Application.Interfaces.Persistence;
using Domain.Common;
using Domain.Exceptions;

namespace Application.UseCases.DailyLoads.GetAthleteWeek;

public sealed record GetAthleteWeekQuery(Guid AthleteId, DateOnly? Date = null);

// Semana del atleta con los días completados (KAN-446): lee las cargas del día de la
// semana y marca qué días registró el atleta. Solo lectura, sin SaveChanges.
public sealed class GetAthleteWeekQueryHandler(IDailyLoadRepository repository)
{
    public async Task<AthleteWeekDto> HandleAsync(
        GetAthleteWeekQuery query, CancellationToken cancellationToken = default)
    {
        if (query.AthleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es inválido.");

        var anchor = query.Date ?? ArgentinaTime.Today;
        var weekStart = anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7));
        var weekEnd = weekStart.AddDays(6);

        var loads = await repository.GetByAthleteAndDateRangeAsync(
            query.AthleteId, weekStart, weekEnd, cancellationToken);

        var completed = loads
            .Select(load => DateOnly.FromDateTime(load.Date))
            .ToHashSet();

        var days = Enumerable.Range(0, 7)
            .Select(offset =>
            {
                var date = weekStart.AddDays(offset);
                return new AthleteWeekDayDto(date, date.DayOfWeek, completed.Contains(date));
            })
            .ToList();

        return new AthleteWeekDto(
            weekStart,
            weekEnd,
            days.Where(day => day.Completed).Select(day => day.Date).ToList(),
            days);
    }
}
