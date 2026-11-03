using Application.Interfaces.Services;

namespace Infrastructure.Services;

public sealed class DefaultFitnessTestMeasurementService : IFitnessTestMeasurementService
{
    public Task<IReadOnlyList<AthleteTestMeasurement>> GetMeasurementsByAthletesAsync(
        IReadOnlyCollection<Guid> athleteIds,
        CancellationToken cancellationToken = default)
    {
        // En producción, retorna lista vacía hasta que el microservicio de Seguimiento o Entrenamiento
        // incorpore la persistencia de resultados individuales de tests físicos.
        return Task.FromResult<IReadOnlyList<AthleteTestMeasurement>>([]);
    }
}

