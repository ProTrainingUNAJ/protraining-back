using Application.DTOs.CheckIns;
using Application.Interfaces.Persistence;
using Application.Mappings;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Services;

namespace Application.UseCases.CheckIns.SubmitCheckIn;

// Respuesta del caso de uso: el wrapper CheckInResponse del contrato + si se creó el registro del día.
public sealed record SubmitCheckInResult(CheckInResponseDto Response, bool Created);

// Registra el check-in del día (upsert por atleta y fecha, estructura base de KAN-329) y calcula
// el semáforo con el motor puro del Dominio: toda la evaluación usa solo datos locales de esta
// base, sin llamadas a otros servicios aunque el Asistente esté caído (KAN-364).
public sealed class SubmitCheckInCommandHandler(
    ICheckInRepository repository,
    IUnitOfWork unitOfWork,
    TrafficLightEngine trafficLightEngine)
{
    public async Task<SubmitCheckInResult> HandleAsync(SubmitCheckInCommand command, CancellationToken cancellationToken = default)
    {
        Validate(command);

        var date = DateTime.UtcNow.Date;

        var checkIn = await repository.GetByAthleteAndDateAsync(command.AthleteId, date, cancellationToken);

        var created = checkIn is null;

        if (created)
        {
            checkIn = new CheckIn
            {
                AthleteId = command.AthleteId,
                Date = date,
                SleepHours = command.SleepHours,
                SleepQuality = command.SleepQuality,
                MuscleSoreness = command.MuscleSoreness,
                FatigueLevel = command.FatigueLevel,
                MoodRate = command.MoodRate,
                StressLevel = command.StressLevel,
                IsSick = command.IsSick,
                EffortRate = command.EffortRate,
                PainZones = command.PainZones?.Trim(),
                HasActiveInjury = command.HasActiveInjury
            };

            await repository.AddAsync(checkIn, cancellationToken);
        }
        else
        {
            checkIn!.SleepHours = command.SleepHours;
            checkIn.SleepQuality = command.SleepQuality;
            checkIn.MuscleSoreness = command.MuscleSoreness;
            checkIn.FatigueLevel = command.FatigueLevel;
            checkIn.MoodRate = command.MoodRate;
            checkIn.StressLevel = command.StressLevel;
            checkIn.IsSick = command.IsSick;
            checkIn.EffortRate = command.EffortRate;
            checkIn.PainZones = command.PainZones?.Trim();
            checkIn.HasActiveInjury = command.HasActiveInjury;
        }

        // Semáforo: motor puro con los datos recién cargados, sin ninguna llamada externa.
        // has_active_injury obliga Rojo ("Lesión activa"); enfermedad y demás señales → Amarillo.
        var evaluation = trafficLightEngine.Evaluate(
            new CheckInData
            {
                SleepHours = checkIn.SleepHours,
                IsSick = checkIn.IsSick,
                EffortRate = checkIn.EffortRate,
                PainZones = checkIn.PainZones,
                HasActiveInjury = checkIn.HasActiveInjury
            },
            context: null);

        checkIn.TrafficLight = evaluation.TriggeredLight;
        checkIn.TrafficLightReason = evaluation.Reason;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var response = new CheckInResponseDto(
            "Check-in completado exitosamente.",
            evaluation.TriggeredLight.ToString(),
            evaluation.Reason,
            CheckInMappings.ToDto(checkIn));

        return new SubmitCheckInResult(response, created);
    }

    // Validación de rangos según el contrato: DomainException → 400 con ProblemDetails (ExceptionMiddleware).
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
