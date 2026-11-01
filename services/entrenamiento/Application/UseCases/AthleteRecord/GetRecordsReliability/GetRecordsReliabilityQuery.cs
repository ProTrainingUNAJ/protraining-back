namespace Application.UseCases.AthleteRecord.GetRecordsReliability;

// Consulta el resumen de confiabilidad de los récords de una categoría (KAN-416).
public sealed record GetRecordsReliabilityQuery(Guid CategoryId, Guid? ExerciseId = null);