using Application.DTOs.Trends;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Trends.GetAthleteWeeklyTrends;
using Domain.Common;
using Domain.Services;

namespace Application.UseCases.Trends.GetCategoryWeeklyTrends;

public sealed record GetCategoryWeeklyTrendsQuery(
    Guid CategoryId,
    int? Weeks = null,
    DateOnly? From = null,
    DateOnly? To = null);

/// <summary>
/// Calcula las tendencias semanales grupales de wellness y carga para una categoría (KAN-489 · #194).
/// Nota: el wellness grupal se calcula como promedio de los promedios semanales individuales (G2: igual peso por atleta),
/// a diferencia del endpoint existente de promedios puntuales (<c>GetCategoryAveragesQuery</c>), que promedia check-ins directos.
/// </summary>
public sealed class GetCategoryWeeklyTrendsQueryHandler(
    IStructureClient structureClient,
    ICheckInRepository checkInRepository,
    IDailyLoadRepository dailyLoadRepository,
    TimeProvider? timeProvider = null)
{
    public async Task<CategoryWeeklyTrendsResponseDto> HandleAsync(
        GetCategoryWeeklyTrendsQuery query,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(ArgentinaTime.FromUtc((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime));
        var window = WeeklyTrendsCalculator.ResolveWindow(today, query.Weeks, query.From, query.To);

        // Valida propiedad de la categoría para el PF autenticado (404 si es ajena o inexistente).
        var athletes = await structureClient.GetCategoryAthletesAsync(query.CategoryId, cancellationToken);
        var athleteIds = athletes
            .Select(a => a.Id)
            .Distinct()
            .ToList();

        if (athleteIds.Count == 0)
        {
            var emptyPoints = window.Weeks
                .Select(w => new CategoryWeeklyTrendPointDto(
                    w.WeekStart,
                    w.WeekEnd,
                    AverageWellness: null,
                    TotalLoad: null,
                    TotalMinutes: null,
                    CheckInCount: 0,
                    LoadCount: 0,
                    ActiveReportingAthletes: 0,
                    WellnessBreakdown: null))
                .ToList();

            return new CategoryWeeklyTrendsResponseDto(
                query.CategoryId,
                0,
                window.From,
                window.To,
                emptyPoints);
        }

        var queryFrom = window.Weeks[0].WeekStart;
        var queryTo = window.To;

        var checkIns = await checkInRepository.GetByAthletesAndDateRangeAsync(
            athleteIds,
            queryFrom.ToDateTime(TimeOnly.MinValue),
            queryTo.ToDateTime(TimeOnly.MaxValue),
            cancellationToken);

        var dailyLoads = await dailyLoadRepository.GetByAthletesAndDateRangeAsync(
            athleteIds,
            queryFrom,
            queryTo,
            cancellationToken);

        var points = WeeklyTrendsCalculator.ComputeCategoryTrends(window, athleteIds, checkIns, dailyLoads)
            .Select(p => new CategoryWeeklyTrendPointDto(
                p.WeekStartDate,
                p.WeekEndDate,
                p.AverageWellness,
                p.TotalLoad,
                p.TotalMinutes,
                p.CheckInCount,
                p.LoadCount,
                p.ActiveReportingAthletes,
                GetAthleteWeeklyTrendsQueryHandler.MapBreakdown(p.Breakdown)))
            .ToList();

        return new CategoryWeeklyTrendsResponseDto(
            query.CategoryId,
            athleteIds.Count,
            window.From,
            window.To,
            points);
    }
}
