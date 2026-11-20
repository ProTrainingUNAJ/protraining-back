namespace Application.Interfaces.Services;

/// <summary>
/// Fuente del momento "día pesado de entrenamiento" para recomendaciones de hábitos (KAN-494).
/// Un valor <c>null</c> significa "no determinable": el motor lo reporta en <c>missingData</c>
/// y NUNCA lo infiere de carga, 1RM ni objetivos de plan.
/// </summary>
/// <remarks>
/// Dependencia pendiente: hoy no hay definición funcional aprobada (fuente, umbral de carga/RPE/volumen)
/// ni datos disponibles para clasificar un día como pesado. La implementación actual devuelve null.
/// Cuando PO/PF definan el criterio, se reemplaza solo esta implementación por DI, sin tocar las reglas
/// ni el contrato. Ver TODO(KAN-494) en HabitsRecommendationRules.
/// </remarks>
public interface IHabitsTrainingDayContextProvider
{
    Task<bool?> IsHeavyTrainingDayAsync(
        Guid athleteId,
        DateOnly date,
        CancellationToken cancellationToken = default);
}
