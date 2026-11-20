using Application.Interfaces.Services;

namespace Infrastructure.Services.Recommendations;

/// <summary>
/// Implementación sin fuente del contexto de peso/objetivo (KAN-494). No inventa valores: devuelve null
/// para que las reglas entreguen una guía cualitativa/derivación. Se conserva como doble de prueba y
/// salida segura ante una futura indisponibilidad de Estructura; la implementación inyectada en
/// producción es StructureHabitsAthleteContextProvider.
/// </summary>
public sealed class NoSourceHabitsAthleteContextProvider : IHabitsAthleteContextProvider
{
    public Task<AthleteNutritionContextDto?> GetAsync(Guid athleteId, CancellationToken cancellationToken = default) =>
        Task.FromResult<AthleteNutritionContextDto?>(null);
}
