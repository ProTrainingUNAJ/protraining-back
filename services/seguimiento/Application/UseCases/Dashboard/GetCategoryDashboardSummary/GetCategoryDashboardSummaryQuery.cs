using Application.DTOs.Dashboard;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Exceptions;
using Domain.Services;

namespace Application.UseCases.Dashboard.GetCategoryDashboardSummary;

public sealed record GetCategoryDashboardSummaryQuery(
    Guid CategoryId,
    DateOnly From,
    DateOnly To);

// Resumen ejecutivo de una categoría en un período (KAN-504). La propiedad de la
// categoría se valida contra el PF del token antes de consultar cualquier métrica.
// No existe modelo de sesiones programadas: los días evaluables son los que tienen
// alguna carga o corrección de asistencia dentro del rango.
public sealed class GetCategoryDashboardSummaryQueryHandler(
    IStructureClient structureClient,
    IAttendanceRepository attendanceRepository,
    IDailyLoadRepository dailyLoadRepository,
    IAthleteInjuryRepository injuryRepository)
{
    public async Task<CategoryDashboardSummaryDto> HandleAsync(
        GetCategoryDashboardSummaryQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.From > query.To)
            throw new DomainException("La fecha 'desde' no puede ser posterior a la fecha 'hasta'.");

        if (query.To.DayNumber - query.From.DayNumber > WeeklyTrendsCalculator.MaxRangeDays)
            throw new DomainException($"El rango máximo permitido es de {WeeklyTrendsCalculator.MaxRangeDays} días.");

        if (!await structureClient.IsOwnCategoryAsync(query.CategoryId, cancellationToken))
            throw new CategoryNotFoundException();

        var athletes = await structureClient.GetCategoryAthletesAsync(query.CategoryId, cancellationToken);
        var athleteIds = athletes.Select(athlete => athlete.Id).Distinct().ToList();

        var injuries = await injuryRepository.GetPeriodAggregateByAthleteIdsAsync(
            athleteIds, query.From, query.To, cancellationToken);

        var injuryMetrics = new CategoryInjuryMetricsDto(
            injuries.ActiveCount,
            injuries.InRecoveryCount,
            injuries.ClearedInPeriodCount,
            injuries.AthletesAffected);

        if (athleteIds.Count == 0)
        {
            return Empty(query, injuryMetrics);
        }

        var corrections = await attendanceRepository.GetByAthletesAndDateRangeAsync(
            athleteIds, query.From, query.To, cancellationToken);
        var loads = await dailyLoadRepository.GetByAthletesAndDateRangeAsync(
            athleteIds, query.From, query.To, cancellationToken);

        var correctionByAthleteDate = corrections
            .GroupBy(correction => (
                AthleteId: correction.AthleteId,
                Date: DateOnly.FromDateTime(correction.Date)))
            .ToDictionary(group => group.Key, group => group.First().IsPresent);

        var loadDatesByAthlete = loads
            .GroupBy(load => load.AthleteId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(load => DateOnly.FromDateTime(load.Date)).ToHashSet());

        var evaluatedDates = correctionByAthleteDate.Keys
            .Select(key => key.Date)
            .Concat(loadDatesByAthlete.Values.SelectMany(dates => dates))
            .Distinct()
            .OrderBy(date => date)
            .ToList();

        var presentDays = 0;
        var absentDays = 0;
        var withoutRecordDays = 0;

        foreach (var athleteId in athleteIds)
        {
            loadDatesByAthlete.TryGetValue(athleteId, out var athleteLoadDates);

            foreach (var date in evaluatedDates)
            {
                // La corrección manual del PF prevalece sobre la carga registrada, igual
                // que en la asistencia automática del día (KAN-445).
                if (correctionByAthleteDate.TryGetValue((athleteId, date), out var isPresent))
                {
                    if (isPresent)
                        presentDays++;
                    else
                        absentDays++;
                }
                else if (athleteLoadDates?.Contains(date) == true)
                {
                    presentDays++;
                }
                else
                {
                    withoutRecordDays++;
                }
            }
        }

        var evaluatedAthleteDays = athleteIds.Count * evaluatedDates.Count;
        var attendance = new CategoryAttendanceMetricsDto(
            presentDays,
            absentDays,
            withoutRecordDays,
            evaluatedAthleteDays,
            evaluatedAthleteDays == 0 ? 0d : (double)presentDays / evaluatedAthleteDays * 100d);

        var weeks = loads
            .GroupBy(load => WeeklyTrendsCalculator.GetWeekStart(DateOnly.FromDateTime(load.Date)))
            .OrderBy(group => group.Key)
            .Select(group => new CategoryDashboardLoadWeekDto(
                group.Key,
                group.Key.AddDays(6),
                group.Sum(load => load.TotalLoad),
                group.Sum(load => load.TotalMinutes),
                group.Count()))
            .ToList();

        var loadMetrics = new CategoryLoadMetricsDto(
            loads.Sum(load => load.TotalLoad),
            loads.Sum(load => load.TotalMinutes),
            loads.Count,
            weeks);

        return new CategoryDashboardSummaryDto(
            query.CategoryId,
            query.From,
            query.To,
            athleteIds.Count,
            evaluatedDates.Count,
            attendance,
            injuryMetrics,
            loadMetrics);
    }

    private static CategoryDashboardSummaryDto Empty(
        GetCategoryDashboardSummaryQuery query,
        CategoryInjuryMetricsDto injuries) =>
        new(
            query.CategoryId,
            query.From,
            query.To,
            0,
            0,
            new CategoryAttendanceMetricsDto(0, 0, 0, 0, 0d),
            injuries,
            new CategoryLoadMetricsDto(0, 0, 0, Array.Empty<CategoryDashboardLoadWeekDto>()));
}
