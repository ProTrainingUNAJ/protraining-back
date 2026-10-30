namespace Application.Exceptions;

// El atleta todavía no cargó minutos y RPE para el día de hoy (404).
public sealed class DailyLoadNotFoundException()
    : Exception("No hay carga del día para este atleta.");
