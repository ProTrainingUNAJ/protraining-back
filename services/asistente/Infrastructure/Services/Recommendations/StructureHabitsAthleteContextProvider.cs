using Application.Interfaces.Services;

namespace Infrastructure.Services.Recommendations;

/// <summary>
/// Fuente real de peso corporal y objetivo deportivo para KAN-494: el perfil físico canónico
/// publicado por Estructura. Reemplaza a NoSourceHabitsAthleteContextProvider por DI;
/// las reglas, el prompt y el snapshot no cambian.
/// </summary>
/// <remarks>
/// Fail-safe: si Estructura no responde o el atleta no pertenece al PF, devuelve null y el motor
/// responde sin cantidades. Nunca se usa 1RM, carga de entrenamiento ni objetivos de plan.
/// </remarks>
public sealed class StructureHabitsAthleteContextProvider(IStructureAthleteClient structureClient)
    : IHabitsAthleteContextProvider
{
    public async Task<AthleteNutritionContextDto?> GetAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await structureClient.GetAthleteProfileAsync(athleteId, cancellationToken);
            return profile is null
                ? null
                : new AthleteNutritionContextDto(profile.AthleticGoal, profile.WeightKg);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Fail-safe: ante cualquier error de Estructura se responde sin cantidades, nunca con valores inventados.
            return null;
        }
    }
}
