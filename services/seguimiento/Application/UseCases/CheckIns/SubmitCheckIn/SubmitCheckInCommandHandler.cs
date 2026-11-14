using Application.DTOs.CheckIns;
using Application.Interfaces.Persistence;
using Application.Mappings;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Services;

namespace Application.UseCases.CheckIns.SubmitCheckIn;

public sealed record SubmitCheckInResult(CheckInResponseDto Response, bool Created);

public sealed class SubmitCheckInCommandHandler(
    ICheckInRepository repository,
    ICheckInInjuryPainRepository injuryPainRepository,
    IAthleteInjuryRepository injuryRepository,
    IUnitOfWork unitOfWork,
    TrafficLightEngine trafficLightEngine)
{
    public async Task<SubmitCheckInResult> HandleAsync(SubmitCheckInCommand command, CancellationToken cancellationToken = default)
    {
        Validate(command);

        var activeInjuries = await injuryRepository.ListNotClearedByAthleteAsync(command.AthleteId, cancellationToken);

        var pains = ValidateInjuryPains(command.Injuries, activeInjuries);

        // Normalización de sueño (KAN-560): el front ofrece hasta 12 h y "Más de 12";
        // cualquier valor > 12 se guarda como 13 (marca de "más de 12"). De 0 a 12 se guarda tal cual.
        double sleepHours = command.SleepHours > MaxSelectableSleepHours
            ? OverLimitSleepHours
            : command.SleepHours;

        // La fecha puede venir del front (sincronización de un check-in guardado sin conexión):
        // se conserva el día en que se hizo, no el de la sincronización. Si no viene, es hoy (Argentina).
        var today = ArgentinaTime.Today;
        var checkInDate = command.Date ?? today;
        if (checkInDate > today)
            throw new DomainException("La fecha del check-in no puede ser futura.");

        var date = checkInDate.ToDateTime(TimeOnly.MinValue);
        var checkIn = await repository.GetByAthleteAndDateAsync(command.AthleteId, date, cancellationToken);

        var created = checkIn is null;

        if (created)
        {
            checkIn = new CheckIn
            {
                AthleteId = command.AthleteId,
                Date = date,
                SleepHours = sleepHours,
                SleepQuality = command.SleepQuality,
                MuscleSoreness = command.MuscleSoreness,
                FatigueLevel = command.FatigueLevel,
                MoodRate = command.MoodRate,
                StressLevel = command.StressLevel,
                IsSick = command.IsSick,
                EffortRate = command.EffortRate,
                PainZones = command.PainZones?.Trim(),
                HasActiveInjury = command.HasActiveInjury || activeInjuries.Count > 0
            };

            await repository.AddAsync(checkIn, cancellationToken);
        }
        else
        {
            checkIn!.SleepHours = sleepHours;
            checkIn.SleepQuality = command.SleepQuality;
            checkIn.MuscleSoreness = command.MuscleSoreness;
            checkIn.FatigueLevel = command.FatigueLevel;
            checkIn.MoodRate = command.MoodRate;
            checkIn.StressLevel = command.StressLevel;
            checkIn.IsSick = command.IsSick;
            checkIn.EffortRate = command.EffortRate;
            checkIn.PainZones = command.PainZones?.Trim();
            checkIn.HasActiveInjury = command.HasActiveInjury || activeInjuries.Count > 0;
        }

        await injuryPainRepository.SetForCheckInAsync(checkIn.Id, pains, cancellationToken);

        // Ventana de 7 días de sueño (KAN-560): anclada a la fecha del check-in (no a "hoy"),
        // para que también funcione con sincronizaciones offline con fechas pasadas. Los días
        // previos salen del repositorio local; el día de hoy se toma del check-in recién armado,
        // porque su fila todavía no está guardada en la base.
        var windowStart = checkInDate.AddDays(-(TrafficLightEngine.ProlongedSleepWindowDays - 1))
            .ToDateTime(TimeOnly.MinValue);
        var previousCheckIns = await repository.GetByAthleteAndDateRangeAsync(
            command.AthleteId, windowStart, date.AddDays(-1), limit: null, cancellationToken);

        var sleepWindow = previousCheckIns
            .Select(c => new SleepWindowEntry { SleepHours = c.SleepHours, IsSick = c.IsSick })
            .ToList();
        sleepWindow.Add(new SleepWindowEntry { SleepHours = checkIn.SleepHours, IsSick = checkIn.IsSick });

        // Semáforo: motor puro con los datos recién cargados, sin ninguna llamada externa.
        // has_active_injury o muy poco sueño (< 4 h) obligan Rojo; el resto de señales → Amarillo.
        var evaluation = trafficLightEngine.Evaluate(
            new CheckInData
            {
                SleepHours = checkIn.SleepHours,
                IsSick = checkIn.IsSick,
                EffortRate = checkIn.EffortRate,
                PainZones = checkIn.PainZones,
                HasActiveInjury = checkIn.HasActiveInjury
            },
            new ContextData { SleepWindow = sleepWindow });

        checkIn.TrafficLight = evaluation.TriggeredLight;
        checkIn.TrafficLightReason = evaluation.Reason;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var responsePains = pains
            .Select(p => new CheckInInjuryPainDto(p.InjuryId, activeInjuries.First(i => i.Id == p.InjuryId).Zone, p.Level))
            .ToList();

        var response = new CheckInResponseDto(
            "Check-in completado exitosamente.",
            evaluation.TriggeredLight.ToString(),
            evaluation.Reason,
            CheckInMappings.ToDto(checkIn, responsePains));

        return new SubmitCheckInResult(response, created);
    }

    private static IReadOnlyList<(Guid InjuryId, int Level)> ValidateInjuryPains(
        IReadOnlyList<SubmitInjuryPain>? provided,
        IReadOnlyList<AthleteInjury> activeInjuries)
    {
        var providedList = provided ?? Array.Empty<SubmitInjuryPain>();

        // Sin molestias activas: no se acepta ningún puntaje (no hay nada que puntuar).
        if (activeInjuries.Count == 0)
        {
            if (providedList.Count > 0)
                throw new DomainException("El atleta no tiene molestias activas para puntuar.");
            return Array.Empty<(Guid, int)>();
        }

        // Cada molestia activa debe tener exactamente un puntaje.
        var byInjury = new Dictionary<Guid, int>();
        foreach (var item in providedList)
        {
            if (item.InjuryId == Guid.Empty)
                throw new DomainException("El identificador de la molestia es inválido.");

            if (!activeInjuries.Any(i => i.Id == item.InjuryId))
                throw new DomainException("La molestia indicada no pertenece al atleta.");

            if (item.Level is null)
                throw new DomainException("Falta puntuar el dolor de la molestia.");

            if (item.Level is < 0 or > 10)
                throw new DomainException("El dolor de cada molestia debe estar entre 0 y 10.");

            if (byInjury.ContainsKey(item.InjuryId))
                throw new DomainException("Hay molestias repetidas en el check-in.");

            byInjury[item.InjuryId] = item.Level.Value;
        }

        foreach (var injury in activeInjuries)
        {
            if (!byInjury.ContainsKey(injury.Id))
                throw new DomainException($"Falta puntuar el dolor de la molestia en {injury.Zone}.");
        }

        return byInjury.Select(kvp => (kvp.Key, kvp.Value)).ToList();
    }

    private const double MaxSelectableSleepHours = 12.0;
    private const double OverLimitSleepHours = 13.0;

    private static void Validate(SubmitCheckInCommand command)
    {
        if (command.AthleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es inválido.");

        if (command.SleepHours is < 0 or > 24)
            throw new DomainException("Las horas de sueño deben estar entre 0 y 24.");

        ValidateScale(command.SleepQuality, "La calidad de sueño");
        ValidateScale(command.MuscleSoreness, "El dolor muscular");
        ValidateScale(command.FatigueLevel, "La fatiga");
        ValidateScale(command.MoodRate, "El ánimo");
        ValidateScale(command.StressLevel, "El estrés");

        if (command.EffortRate is < 1 or > 10)
            throw new DomainException("El esfuerzo debe estar entre 1 y 10.");
    }

    private static void ValidateScale(int value, string field)
    {
        if (value is < 1 or > 5)
            throw new DomainException($"{field} debe estar entre 1 y 5.");
    }
}
