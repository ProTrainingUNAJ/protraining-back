namespace Application.Interfaces.Services;

public sealed record AthleteTestMeasurement(
    Guid AthleteId,
    string TestType,
    double Value,
    string Unit,
    DateTime Date);

/// <summary>
/// Proveedor de mediciones de pruebas físicas por atleta.
/// </summary>
public interface IFitnessTestMeasurementService
{
    Task<IReadOnlyList<AthleteTestMeasurement>> GetMeasurementsByAthletesAsync(
        IReadOnlyCollection<Guid> athleteIds,
        CancellationToken cancellationToken = default);
}

