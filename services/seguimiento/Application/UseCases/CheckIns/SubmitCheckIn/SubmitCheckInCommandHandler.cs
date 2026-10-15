using Application.DTOs.CheckIns;
using Application.Interfaces.Persistence;
using Application.Mappings;
using Domain.Entities;

namespace Application.UseCases.CheckIns.SubmitCheckIn;

public sealed record SubmitCheckInResult(CheckInDto CheckIn, bool Created);

public sealed class SubmitCheckInCommandHandler(ICheckInRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<SubmitCheckInResult> HandleAsync(SubmitCheckInCommand command, CancellationToken cancellationToken = default)
    {
        var date = DateTime.UtcNow.Date;

        var checkIn = await repository.GetByAthleteAndDateAsync(command.AthleteId, date, cancellationToken);

        var created = checkIn is null;

        if (created)
        {
            checkIn = new CheckIn
            {
                AthleteId = command.AthleteId,
                Date = date,
                // Placeholder: el cálculo del semáforo es de otra tarea.
                TrafficLight = TrafficLight.Green,
                TrafficLightReason = null,
                SleepHours = command.SleepHours,
                SleepQuality = command.SleepQuality,
                MuscleSoreness = command.MuscleSoreness,
                FatigueLevel = command.FatigueLevel,
                MoodRate = command.MoodRate,
                StressLevel = command.StressLevel,
                IsSick = command.IsSick,
                EffortRate = command.EffortRate,
                PainZones = command.PainZones?.Trim()
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
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new SubmitCheckInResult(CheckInMappings.ToDto(checkIn!), created);
    }
}