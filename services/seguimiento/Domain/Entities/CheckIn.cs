using Domain.Common;

namespace Domain.Entities;

public class CheckIn : BaseEntity
{
    public Guid AthleteId { get; set; }
    public DateTime Date { get; set; }
    public TrafficLight TrafficLight { get; set; }
    public string? TrafficLightReason { get; set; }
    public double SleepHours { get; set; }
    public int SleepQuality { get; set; }
    public int MuscleSoreness { get; set; }
    public int FatigueLevel { get; set; }
    public int MoodRate { get; set; }
    public int StressLevel { get; set; }
    public bool IsSick { get; set; }
    public int? EffortRate { get; set; }
    public string? PainZones { get; set; }
}

public enum TrafficLight { Green, Yellow, Red }