namespace Application.Interfaces.Services;

/// <summary>
/// Peso corporal y objetivo deportivo disponibles para el cálculo de proteína.
/// Valores null significan "dato no disponible": el motor debe responder sin cantidades.
/// </summary>
public sealed record AthleteNutritionContextDto(string? AthleticGoal, double? WeightKg);

/// <summary>
/// Fuente de contexto antropométrico/deportivo del atleta para recomendaciones de hábitos.
/// </summary>
/// <remarks>
/// Fuente canónica: Estructura expone peso y objetivo en el perfil físico del atleta
/// (PATCH /api/v1/athletes/{id}/physical-profile y PATCH /api/v1/me/physical-profile).
/// La implementación inyectada es StructureHabitsAthleteContextProvider; ante ausencia de dato
/// o error devuelve null y el flujo responde sin cantidades. Nunca se usan 1RM, carga de
/// entrenamiento ni objetivos de plan como reemplazo.
/// </remarks>
public interface IHabitsAthleteContextProvider
{
    Task<AthleteNutritionContextDto?> GetAsync(Guid athleteId, CancellationToken cancellationToken = default);
}
