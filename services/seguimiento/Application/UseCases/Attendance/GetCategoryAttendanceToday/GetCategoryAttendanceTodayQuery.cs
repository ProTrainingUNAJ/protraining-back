using Application.DTOs.Attendance;
using Application.DTOs.Structure;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
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
    private readonly IAttendanceRepository _attendanceRepository;

    public GetCategoryAttendanceTodayQueryHandler(
        IStructureClient structureClient,
        IDailyLoadRepository dailyLoadRepository,
        IAttendanceRepository attendanceRepository)
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
        IReadOnlyList<Domain.Entities.Attendance> corrections;

        try
        {
            athletes = await _structureClient.GetCategoryAthletesAsync(query.CategoryId, cancellationToken);
            loads = await _dailyLoadRepository.GetByAthletesAndDateAsync(
                athletes.Select(athlete => athlete.Id).ToList(),
                date,
                cancellationToken);

            corrections = await _attendanceRepository.GetByAthletesAndDateAsync(
                athletes.Select(athlete => athlete.Id).ToList(),
                date,
                cancellationToken);
        }
        catch (StructureUnavailableException)
        {
            // Resiliencia (KAN-445): Estructura caída no puede tumbar la asistencia del PF.
            var todaysLoads = await _dailyLoadRepository.GetByDateAsync(date, cancellationToken);
            var todaysCorrections = await _attendanceRepository.GetByDateAsync(date, cancellationToken);

            var automaticPresent = todaysLoads.Select(load => load.AthleteId).ToHashSet();
            var correctionByAthlete = todaysCorrections.ToDictionary(c => c.AthleteId);

            var degradedIds = automaticPresent
                .Union(todaysCorrections.Select(c => c.AthleteId))
                .ToList();

            var degradedLoadByAthlete = todaysLoads.ToDictionary(l => l.AthleteId);
            var degradedSummaries = degradedIds.Select(id =>
            {
                var isPresent = correctionByAthlete.TryGetValue(id, out var correction)
                    ? correction.IsPresent
                    : automaticPresent.Contains(id);
                degradedLoadByAthlete.TryGetValue(id, out var load);

                var activities = new List<AttendanceActivityDetailDto>();
                if (load is not null)
                {
                    if (load.GymMinutes > 0)
                        activities.Add(new AttendanceActivityDetailDto("Gym", "Gimnasio", load.GymMinutes, load.GymRpe));
                    if (load.SportTrainingMinutes > 0)
                        activities.Add(new AttendanceActivityDetailDto("SportTraining", "Entrenamiento", load.SportTrainingMinutes, load.SportTrainingRpe));
                    if (load.Match1Minutes > 0)
                        activities.Add(new AttendanceActivityDetailDto("Match1", "Partido 1", load.Match1Minutes, load.Match1Rpe));
                    if (load.Match2Minutes > 0)
                        activities.Add(new AttendanceActivityDetailDto("Match2", "Partido 2", load.Match2Minutes, load.Match2Rpe));
                    if (load.IndividualWorkMinutes > 0)
                        activities.Add(new AttendanceActivityDetailDto("IndividualWork", "Trabajo Individual", load.IndividualWorkMinutes, load.IndividualWorkRpe));
                }

                return new AthleteAttendanceSummaryDto(
                    id,
                    string.Empty,
                    isPresent ? "Present" : "NoRecord",
                    load?.TotalMinutes ?? 0,
                    load?.TotalLoad ?? 0,
                    activities);
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

        var loadByAthlete = loads.ToDictionary(l => l.AthleteId);

        var athleteSummaries = athletes.Select(athlete =>
        {
            var correction = corrections.FirstOrDefault(c => c.AthleteId == athlete.Id);
            loadByAthlete.TryGetValue(athlete.Id, out var load);
            var isPresent = correction is not null
                ? correction.IsPresent
                : load is not null;

            var activities = new List<AttendanceActivityDetailDto>();
            if (load is not null)
            {
                if (load.GymMinutes > 0)
                    activities.Add(new AttendanceActivityDetailDto("Gym", "Gimnasio", load.GymMinutes, load.GymRpe));
                if (load.SportTrainingMinutes > 0)
                    activities.Add(new AttendanceActivityDetailDto("SportTraining", "Entrenamiento", load.SportTrainingMinutes, load.SportTrainingRpe));
                if (load.Match1Minutes > 0)
                    activities.Add(new AttendanceActivityDetailDto("Match1", "Partido 1", load.Match1Minutes, load.Match1Rpe));
                if (load.Match2Minutes > 0)
                    activities.Add(new AttendanceActivityDetailDto("Match2", "Partido 2", load.Match2Minutes, load.Match2Rpe));
                if (load.IndividualWorkMinutes > 0)
                    activities.Add(new AttendanceActivityDetailDto("IndividualWork", "Trabajo Individual", load.IndividualWorkMinutes, load.IndividualWorkRpe));
            }

            return new AthleteAttendanceSummaryDto(
                athlete.Id,
                athlete.FullName,
                isPresent ? "Present" : "NoRecord",
                load?.TotalMinutes ?? 0,
                load?.TotalLoad ?? 0,
                activities);
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
}