using Application.DTOs.Structure;
using Application.DTOs.TrafficLight;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;
using Domain.Common;
using Domain.Entities;
using Domain.Services;

// El namespace Application.UseCases.TrafficLight sombrea al enum TrafficLight del Dominio:
// el alias desambigua los nameof del orden de severidad.
using TrafficLightColor = Domain.Entities.TrafficLight;

namespace Application.UseCases.TrafficLight.GetCategoryTrafficLightToday;

public sealed record GetCategoryTrafficLightTodayQuery(Guid CategoryId);

// Semáforo del día por categoría (pantalla del PF, KAN-364): cruza los atletas que devuelve
// Estructura con los check-ins locales de hoy (hora Argentina). Si Estructura no responde,
// degrada y devuelve al menos los check-ins ya registrados, sin tumbar la ejecución.
public sealed class GetCategoryTrafficLightTodayQueryHandler(
    IStructureClient structureClient,
    ICheckInRepository checkInRepository,
    TrafficLightEngine trafficLightEngine)
{
    public async Task<IReadOnlyList<AthleteTrafficLightSummaryDto>> HandleAsync(
        GetCategoryTrafficLightTodayQuery query,
        CancellationToken cancellationToken = default)
    {
        // El día se calcula en hora Argentina, igual que al registrar el check-in.
        var date = ArgentinaTime.Today.ToDateTime(TimeOnly.MinValue);

        IReadOnlyList<CategoryAthleteDto> athletes;
        IReadOnlyList<CheckIn> checkIns;

        try
        {
            athletes = await structureClient.GetCategoryAthletesAsync(query.CategoryId, cancellationToken);
            checkIns = await checkInRepository.GetByAthletesAndDateAsync(
                athletes.Select(athlete => athlete.Id).ToList(),
                date,
                cancellationToken);
        }
        catch (StructureUnavailableException)
        {
            // Resiliencia (KAN-364): Estructura caída no puede tumbar el semáforo del PF.
            // Se devuelven los check-ins ya registrados hoy, aunque no tengamos sus nombres.
            var todaysCheckIns = await checkInRepository.GetByDateAsync(date, cancellationToken);

            return todaysCheckIns
                .Select(checkIn => new AthleteTrafficLightSummaryDto(
                    checkIn.AthleteId,
                    AthleteName: string.Empty,
                    checkIn.TrafficLight.ToString(),
                    checkIn.TrafficLightReason,
                    HasReportedToday: true,
                    checkIn.CreatedAt,
                    Score(checkIn.TrafficLight.ToString()),
                    CheckInMappings.ToIndicatorsDto(checkIn)))
                .ToList();
        }

        var checkInsByAthlete = checkIns.ToDictionary(checkIn => checkIn.AthleteId);

        var summary = new List<AthleteTrafficLightSummaryDto>(athletes.Count);

        foreach (var athlete in athletes)
        {
            if (checkInsByAthlete.TryGetValue(athlete.Id, out var checkIn))
            {
                // Ya reportó hoy: el color y el motivo salen del check-in persistido,
                // y los indicadores traen sus valores crudos para la card (KAN-365).
                summary.Add(new AthleteTrafficLightSummaryDto(
                    athlete.Id,
                    athlete.FullName,
                    checkIn.TrafficLight.ToString(),
                    checkIn.TrafficLightReason,
                    HasReportedToday: true,
                    checkIn.CreatedAt,
                    Score(checkIn.TrafficLight.ToString()),
                    CheckInMappings.ToIndicatorsDto(checkIn)));
            }
            else
            {
                // Sin check-in: el motor con entrada null produce Amarillo "sin check-in",
                // y los indicadores quedan null para que el front pinte gris "pendiente".
                var evaluation = trafficLightEngine.Evaluate(checkInData: null, context: null);

                summary.Add(new AthleteTrafficLightSummaryDto(
                    athlete.Id,
                    athlete.FullName,
                    evaluation.TriggeredLight.ToString(),
                    evaluation.Reason,
                    HasReportedToday: false,
                    ReportedAt: null,
                    Score(evaluation.TriggeredLight.ToString()),
                    Indicators: null));
            }
        }

        // Severidad Rojo → Amarillo → Verde. OrderBy es estable: dentro de cada grupo se
        // conserva el orden por apellido que trae Estructura.
        return summary
            .OrderBy(item => Severity(item.TrafficLight))
            .ToList();
    }

    private static int Severity(string trafficLight) => trafficLight switch
    {
        nameof(TrafficLightColor.Red) => 0,
        nameof(TrafficLightColor.Yellow) => 1,
        _ => 2
    };

    // Escala del motor (KAN-363/364): Rojo 20, Amarillo 50, Verde 90. Se deriva del color
    // persistido en vez de re-evaluar el motor, para no contradecir el color ya guardado.
    private static int Score(string trafficLight) => trafficLight switch
    {
        nameof(TrafficLightColor.Red) => 20,
        nameof(TrafficLightColor.Yellow) => 50,
        _ => 90
    };
}
