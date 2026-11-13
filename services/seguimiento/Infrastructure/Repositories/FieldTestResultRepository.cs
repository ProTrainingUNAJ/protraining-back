using Application.DTOs.FitnessTests;
using Application.Interfaces.Persistence;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class FieldTestResultRepository(AppDbContext context) : IFieldTestResultRepository
{
    public async Task<IReadOnlyList<AthleteFitnessTestDto>> ListByAthleteAndDateRangeAsync(
        Guid athleteId,
        Guid? catalogTestId = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
        {
            var query = context.FieldTestResults
                .AsNoTracking()
                .Where(r => r.AthleteId == athleteId);

            if (catalogTestId.HasValue)
                query = query.Where(r => r.CatalogTestId == catalogTestId.Value);

            if (from.HasValue)
                query = query.Where(r => r.RecordedAt >= from.Value);

            if (to.HasValue)
                query = query.Where(r => r.RecordedAt <= to.Value);

            var joined = await query
                .OrderBy(r => r.RecordedAt)
                .ThenBy(r => r.Attempt)
                .Join(
                    context.CatalogTests.AsNoTracking(),
                    r => r.CatalogTestId,
                    t => t.Id,
                    (r, t) => new AthleteFitnessTestDto(
                        r.Id,
                        r.AthleteId,
                        r.CatalogTestId,
                        t.Name,
                        t.MotorCapacity.ToString(),
                        r.Value,
                        t.MeasurementUnit,
                        r.Attempt,
                        r.RecordedAt))
                .ToListAsync(cancellationToken);

            return joined;
        });
}

