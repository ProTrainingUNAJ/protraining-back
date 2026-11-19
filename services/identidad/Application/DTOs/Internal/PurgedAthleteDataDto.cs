namespace Application.DTOs.Internal;

/// <summary>
/// Resumen de registros físicos eliminados de un atleta en Identidad (#204 · KAN-499).
/// </summary>
public sealed record PurgedAthleteDataDto(
    Guid AthleteId,
    int UsersCount,
    int RefreshTokensCount,
    int PhotosCount,
    int TotalRecordsPurged);
