using Application.DTOs.CheckIns;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Services;

namespace Application.UseCases.CheckIns.SubmitCheckIn;

// Registra el check-in del día y calcula el semáforo con el motor puro del Dominio.
// Toda la evaluación usa solo datos locales (esta base): si el Asistente u otro servicio está caído,
// este flujo no se entera y sigue respondiendo igual (KAN-364).
public sealed class SubmitCheckInCommandHandler
{
    private readonly ICheckInRepository _checkIns;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TrafficLightEngine _trafficLightEngine;

    public SubmitCheckInCommandHandler(
        ICheckInRepository checkIns,
        IUnitOfWork unitOfWork,
        TrafficLightEngine trafficLightEngine)
    {
        _checkIns = checkIns;
        _unitOfWork = unitOfWork;
        _trafficLightEngine = trafficLightEngine;
    }

    public async Task<CheckInResponseDto> HandleAsync(
        SubmitCheckInCommand command,
        CancellationToken cancellationToken = default)
    {
        CheckInRequestDto request = command.Request;
        Validate(command.AthleteId, request);

        // Un solo check-in por atleta por día (el índice único de check_ins lo garantiza).
        DateTime date = DateTime.UtcNow.Date;

        CheckIn? existing = await _checkIns.GetByAthleteAndDateAsync(command.AthleteId, date, cancellationToken);
        CheckIn checkIn;
        if (existing is null)
        {
            checkIn = NewCheckIn(command.AthleteId, date);
            await _checkIns.AddAsync(checkIn, cancellationToken);
        }
        else
        {
            // Upsert: el registro del día ya existe y sigue trackeado por el contexto.
            checkIn = existing;
        }

        // Aplicar las respuestas del reporte sobre la entidad (upsert).
        checkIn.SleepHours = request.SleepHours;
        checkIn.SleepQuality = request.SleepQuality;
        checkIn.MuscleSoreness = request.MuscleSoreness;
        checkIn.FatigueLevel = request.FatigueLevel;
        checkIn.MoodRate = request.MoodRate;
        checkIn.StressLevel = request.StressLevel;
        checkIn.IsSick = request.IsSick;
        checkIn.EffortRate = request.EffortRate;
        checkIn.PainZones = request.PainZones;
        checkIn.HasActiveInjury = request.HasActiveInjury;

        // Semáforo: motor puro con los datos recién cargados, sin ninguna llamada externa.
        HeuristicEvaluationResult evaluation = _trafficLightEngine.Evaluate(
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

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CheckInResponseDto(
            "Check-in completado exitosamente.",
            evaluation.TriggeredLight.ToString(),
            evaluation.Reason,
            Map(checkIn));
    }

    private static CheckIn NewCheckIn(Guid athleteId, DateTime date) =>
        new()
        {
            AthleteId = athleteId,
            Date = date
        };

    // Validación de rangos según el contrato: DomainException → 400 con ProblemDetails (ExceptionMiddleware).
    private static void Validate(Guid athleteId, CheckInRequestDto request)
    {
        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es inválido.");

        if (request.SleepHours is < 0 or > 24)
            throw new DomainException("Las horas de sueño deben estar entre 0 y 24.");

        ValidateScale(request.SleepQuality, "La calidad de sueño");
        ValidateScale(request.MuscleSoreness, "El dolor muscular");
        ValidateScale(request.FatigueLevel, "La fatiga");
        ValidateScale(request.MoodRate, "El ánimo");
        ValidateScale(request.StressLevel, "El estrés");

        if (request.EffortRate is < 1 or > 10)
            throw new DomainException("El esfuerzo debe estar entre 1 y 10.");
    }

    private static void ValidateScale(int value, string field)
    {
        if (value is < 1 or > 5)
            throw new DomainException($"{field} debe estar entre 1 y 5.");
    }

    private static CheckInDto Map(CheckIn checkIn) => new(
        checkIn.Id,
        checkIn.AthleteId,
        DateOnly.FromDateTime(checkIn.Date),
        checkIn.TrafficLight.ToString(),
        checkIn.TrafficLightReason,
        checkIn.SleepHours,
        checkIn.SleepQuality,
        checkIn.MuscleSoreness,
        checkIn.FatigueLevel,
        checkIn.MoodRate,
        checkIn.StressLevel,
        checkIn.IsSick,
        checkIn.EffortRate,
        checkIn.PainZones,
        checkIn.HasActiveInjury);
}
