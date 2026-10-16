using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.CheckIns;

public sealed record SubmitCheckInRequest
{
    [Range(0, 24, ErrorMessage = "Las horas de sueño deben estar entre 0 y 24.")]
    public double SleepHours { get; init; }

    [Range(1, 5, ErrorMessage = "La calidad de sueño debe estar entre 1 y 5.")]
    public int SleepQuality { get; init; }

    [Range(1, 5, ErrorMessage = "El dolor muscular debe estar entre 1 y 5.")]
    public int MuscleSoreness { get; init; }

    [Range(1, 5, ErrorMessage = "El nivel de fatiga debe estar entre 1 y 5.")]
    public int FatigueLevel { get; init; }

    [Range(1, 5, ErrorMessage = "El estado de ánimo debe estar entre 1 y 5.")]
    public int MoodRate { get; init; }

    [Range(1, 5, ErrorMessage = "El nivel de estrés debe estar entre 1 y 5.")]
    public int StressLevel { get; init; }

    [Range(1, 10, ErrorMessage = "El esfuerzo percibido (RPE) debe estar entre 1 y 10.")]
    public int? EffortRate { get; init; }

    public bool IsSick { get; init; }

    public string? PainZones { get; init; }

    // Lesión activa marcada por el atleta: el semáforo del día queda en Rojo (KAN-364).
    public bool HasActiveInjury { get; init; }
}