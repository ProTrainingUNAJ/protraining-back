namespace Application.DTOs.CheckIns;

// Reporte de bienestar diario del atleta (contrato: CheckInRequest) + lesión activa (KAN-364).
// Los seis campos obligatorios del contrato van como required: si faltan en el JSON, el modelo no arma
// y sale un 400 con ProblemDetails.
public sealed record CheckInRequestDto
{
    // Horas de sueño: rango 0 a 24.
    public required double SleepHours { get; init; }

    // Calidad de descanso de 1 (pésima) a 5 (óptima).
    public required int SleepQuality { get; init; }

    // Dolor muscular/articular de 1 (ninguno) a 5 (severo).
    public required int MuscleSoreness { get; init; }

    // Nivel de fatiga de 1 a 5.
    public required int FatigueLevel { get; init; }

    // Ánimo de 1 a 5.
    public required int MoodRate { get; init; }

    // Estrés de 1 a 5.
    public required int StressLevel { get; init; }

    // Esfuerzo percibido de 1 a 10; opcional en el contrato.
    public int? EffortRate { get; init; }

    public bool IsSick { get; init; }

    public string? PainZones { get; init; }

    // Lesión activa marcada por el atleta: el semáforo del día queda en Rojo (Demo 1, KAN-363/KAN-364).
    public bool HasActiveInjury { get; init; }
}
