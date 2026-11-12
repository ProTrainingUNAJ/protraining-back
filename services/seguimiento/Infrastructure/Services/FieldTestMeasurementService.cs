using Application.Interfaces.Services;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

/// <summary>
/// Mediciones de tests físicos a partir de las marcas cargadas en las sesiones de tests de campo (KAN-424/425).
/// Alimenta la comparación del atleta contra su grupo (KAN-449) y los promedios por categoría (KAN-447).
/// </summary>
public sealed class FieldTestMeasurementService(AppDbContext context) : IFitnessTestMeasurementService
{
    public async Task<IReadOnlyList<AthleteTestMeasurement>> GetMeasurementsByAthletesAsync(
        IReadOnlyCollection<Guid> athleteIds,
        CancellationToken cancellationToken = default)
    {
        if (athleteIds.Count == 0)
            return [];

        var ids = athleteIds.ToList();

        // El filtro global del contexto deja solo las marcas del PF del token y sin las borradas.
        // El nombre y la unidad salen del catálogo, igual que en la evolución del atleta.
        return await PersistenceErrors.ExecuteAsync(async () =>
            await context.FieldTestResults
                .AsNoTracking()
                .Where(result => ids.Contains(result.AthleteId))
                .Join(
                    context.CatalogTests.AsNoTracking(),
                    result => result.CatalogTestId,
                    test => test.Id,
                    (result, test) => new AthleteTestMeasurement(
                        result.AthleteId,
                        test.Name,
                        (double)result.Value,
                        test.MeasurementUnit,
                        result.RecordedAt))
                .ToListAsync(cancellationToken));
    }
}
