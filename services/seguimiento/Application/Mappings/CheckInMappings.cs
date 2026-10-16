using Application.DTOs.CheckIns;
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
}