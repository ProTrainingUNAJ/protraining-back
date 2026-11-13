namespace Application.DTOs.Alerts;

/// <summary>
/// Comando para ejecutar el análisis diario nocturno de sobrecarga y fatiga acumulada (#177 · KAN-472).
/// Permite parametrizar opcionalmente la fecha de análisis (útil para pruebas y corridas puntuales).
/// </summary>
public sealed record ProcessDailyOverloadAnalysisCommand(DateOnly? TargetDate = null);

/// <summary>
/// Resultado métrico de la ejecución del worker diario.
/// </summary>
public sealed record ProcessDailyOverloadAnalysisResult(
    int ProcessedAthletes,
    int GeneratedAlerts,
    int SkippedAlerts);

