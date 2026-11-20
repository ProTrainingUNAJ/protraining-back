namespace Application.DTOs.Injuries;

/// <summary>
/// Resumen liviano de lesiones activas por atleta para evitar consultas N+1 desde la UI de planificaciones (KAN-464).
/// </summary>
public sealed record AthleteActiveInjurySummaryDto(
    Guid AthleteId,
    int ActiveCount);
