namespace Application.DTOs.Injuries;

// Conteos agregados de lesiones de un grupo de atletas en un período. No incluye
// zonas, descripciones ni ningún dato clínico individual (KAN-504).
public sealed record InjuryPeriodAggregateDto(
    int ActiveCount,
    int InRecoveryCount,
    int ClearedInPeriodCount,
    int AthletesAffected);
