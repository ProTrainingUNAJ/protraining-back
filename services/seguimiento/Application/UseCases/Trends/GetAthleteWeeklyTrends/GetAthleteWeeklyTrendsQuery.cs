using Application.DTOs.Trends;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Common;
using Domain.Services;

namespace Application.UseCases.Trends.GetAthleteWeeklyTrends;

public sealed record GetAthleteWeeklyTrendsQuery(
    Guid AthleteId,
    int? Weeks = null,
    DateOnly? From = null,
    DateOnly? To = null);

public sealed class GetAthleteWeeklyTrendsQueryHandler(
    IAthleteAccessGuard athleteAccessGuard,
    ICheckInRepository checkInRepository,
    IDailyLoadRepository dailyLoadRepository,
    TimeProvider? timeProvider = null)
{
    public async Task<AthleteWeeklyTrendsResponseDto> HandleAsync(
        GetAthleteWeeklyTrendsQuery query,
        CancellationToken cancellationToken = default)
    {
        await athleteAccessGuard.EnsureCanReadAsync(query.AthleteId, cancellationToken);

        var today = DateOnly.FromDateTime(ArgentinaTime.FromUtc((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime));
        var window = WeeklyTrendsCalculator.ResolveWindow(today, query.Weeks, query.From, query.To);

        var queryFrom = window.Weeks[0].WeekStart;
        var queryTo = window.To;

        var checkIns = await checkInRepository.GetByAthleteAndDateRangeAsync(
            query.AthleteId,
            startDate: queryFrom.ToDateTime(TimeOnly.MinValue),
            endDate: queryTo.ToDateTime(TimeOnly.MaxValue),
            limit: null,
            cancellationToken: cancellationToken);

        var dailyLoads = await dailyLoadRepository.GetByAthleteAndDateRangeAsync(
            query.AthleteId,
            queryFrom,
            queryTo,
            cancellationToken);

        var points = WeeklyTrendsCalculator.ComputeAthleteTrends(window, checkIns, dailyLoads)
            .Select(p => new WeeklyTrendPointDto(
                p.WeekStartDate,
                p.WeekEndDate,
                p.AverageWellness,
                p.TotalLoad,
                p.TotalMinutes,
                p.CheckInCount,
                p.LoadCount,
                MapBreakdown(p.Breakdown)))
            .ToList();

        return new AthleteWeeklyTrendsResponseDto(
            query.AthleteId,
            window.From,
            window.To,
            points);
    }

    internal static WellnessMetricsBreakdownDto? MapBreakdown(WeeklyTrendsCalculator.WellnessBreakdownResult? breakdown) =>
        breakdown is null
            ? null
            : new WellnessMetricsBreakdownDto(
                breakdown.SleepQuality,
                breakdown.SleepHours,
                breakdown.MuscleSoreness,
                breakdown.FatigueLevel,
                breakdown.MoodRate,
                breakdown.StressLevel);
}
