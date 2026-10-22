using Application.DTOs.CheckIns;
using Application.DTOs.TrafficLight;
using Domain.Entities;

namespace Application.Mappings;

public static class CheckInMappings
{
    public static CheckInDto ToDto(CheckIn checkIn) =>
        new(
            checkIn.Id,
            checkIn.AthleteId,
            checkIn.Date,
            checkIn.TrafficLight.ToString(),
            checkIn.TrafficLightReason,
            checkIn.SleepHours,
            checkIn.SleepQuality,
            checkIn.MuscleSoreness,
            checkIn.FatigueLevel,
            checkIn.MoodRate,
            checkIn.StressLevel,
            checkIn.EffortRate,
            checkIn.IsSick,
            checkIn.PainZones,
            checkIn.HasActiveInjury);

    // Desglose del check-in para los indicadores de la card del semáforo (KAN-365).
    public static TrafficLightIndicatorsDto ToIndicatorsDto(CheckIn checkIn) =>
        new(
            checkIn.SleepHours,
            checkIn.SleepQuality,
            checkIn.EffortRate,
            checkIn.IsSick,
            checkIn.HasActiveInjury,
            checkIn.PainZones);
}