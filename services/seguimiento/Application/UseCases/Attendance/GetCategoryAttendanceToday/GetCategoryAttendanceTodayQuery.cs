using Application.DTOs.Attendance;
using Application.DTOs.Structure;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Common;
using Domain.Entities;

namespace Application.UseCases.Attendance.GetCategoryAttendanceToday;

public sealed record GetCategoryAttendanceTodayQuery(Guid CategoryId);

// Asistencia automática del día por categoría (pantalla del PF, KAN-445): cruza los atletas que
// devuelve Estructura con las cargas del día locales. Si un atleta cargó entrenamiento, figura
// presente; si no, queda como "sin registro". Si Estructura no responde, degrada a las cargas
// ya registradas hoy, sin tumbar la ejecución.
public sealed class GetCategoryAttendanceTodayQueryHandler
{
    private readonly IStructureClient _structureClient;
    private readonly IDailyLoadRepository _dailyLoadRepository;
    private readonly IAttendanceRepository? _attendanceRepository;

    public GetCategoryAttendanceTodayQueryHandler(
        IStructureClient structureClient,
        IDailyLoadRepository dailyLoadRepository)
        : this(structureClient, dailyLoadRepository, null)
    {
    }

    public GetCategoryAttendanceTodayQueryHandler(
        IStructureClient structureClient,
        IDailyLoadRepository dailyLoadRepository,
        IAttendanceRepository? attendanceRepository)
    {
        _structureClient = structureClient;
        _dailyLoadRepository = dailyLoadRepository;
        _attendanceRepository = attendanceRepository;
    }

    public async Task<CategoryAttendanceSummaryDto> HandleAsync(
        GetCategoryAttendanceTodayQuery query,
        CancellationToken cancellationToken = default)
    {
        var date = ArgentinaTime.Today.ToDateTime(TimeOnly.MinValue);

        IReadOnlyList<CategoryAthleteDto> athletes;
        IReadOnlyList<DailyLoad> loads;
        IReadOnlyList<Domain.Entities.Attendance> corrections = Array.Empty<Domain.Entities.Attendance>();

        try
        {
            athletes = await _structureClient.GetCategoryAthletesAsync(query.CategoryId, cancellationToken);
            loads = await _dailyLoadRepository.GetByAthletesAndDateAsync(
                athletes.Select(athlete => athlete.Id).ToList(),
                date,
                cancellationToken);

            if (_attendanceRepository is not null)
            {
                corrections = await _attendanceRepository.GetByAthletesAndDateAsync(
                    athletes.Select(athlete => athlete.Id).ToList(),
                    date,
                    cancellationToken);
            }
        }
        catch (StructureUnavailableException)
        {
            // Resiliencia (KAN-445): Estructura caída no puede tumbar la asistencia del PF.
            var todaysLoads = await _dailyLoadRepository.GetByDateAsync(date, cancellationToken);
            var todaysCorrections = _attendanceRepository is null
                ? Array.Empty<Domain.Entities.Attendance>()
                : await _attendanceRepository.GetByDateAsync(date, cancellationToken);

            var automaticPresent = todaysLoads.Select(load => load.AthleteId).ToHashSet();
            var correctionByAthlete = todaysCorrections.ToDictionary(c => c.AthleteId);

            var degradedIds = automaticPresent
                .Union(todaysCorrections.Select(c => c.AthleteId))
                .ToList();

            var degradedSummaries = degradedIds.Select(id =>
            {
                var isPresent = correctionByAthlete.TryGetValue(id, out var correction)
                    ? correction.IsPresent
                    : automaticPresent.Contains(id);
                return new AthleteAttendanceSummaryDto(id, string.Empty, isPresent ? "Present" : "NoRecord");
            }).ToList();

            var presentCount = degradedSummaries.Count(s => s.Status == "Present");

            return new CategoryAttendanceSummaryDto(
                query.CategoryId,
                ArgentinaTime.Today,
                presentCount,
                presentCount,
                presentCount == 0 ? 0d : 100d,
                degradedSummaries);
        }

        var loadIds = loads.Select(load => load.AthleteId).ToHashSet();

        var athleteSummaries = athletes.Select(athlete =>
        {
            var correction = corrections.FirstOrDefault(c => c.AthleteId == athlete.Id);
            var isPresent = correction is not null
                ? correction.IsPresent
                : loadIds.Contains(athlete.Id);
            return new AthleteAttendanceSummaryDto(
                athlete.Id,
                athlete.FullName,
                isPresent ? "Present" : "NoRecord");
        }).ToList();

        var present = athleteSummaries.Count(a => a.Status == "Present");
        var total = athletes.Count;
        var percentage = total == 0 ? 0d : (double)present / total * 100d;

        return new CategoryAttendanceSummaryDto(
            query.CategoryId,
            ArgentinaTime.Today,
            total,
            present,
            percentage,
            athleteSummaries);
    }

    public Task<CategoryAttendanceSummaryDto> Handle(
        GetCategoryAttendanceTodayQuery query,
        CancellationToken cancellationToken = default) =>
        HandleAsync(query, cancellationToken);
}