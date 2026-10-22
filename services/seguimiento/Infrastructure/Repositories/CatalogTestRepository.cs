using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class CatalogTestRepository(AppDbContext context) : ICatalogTestRepository
{
    public async Task<List<CatalogTest>> ListAsync(
        MotorCapacity? motorCapacity,
        CancellationToken cancellationToken = default) =>
        // El filtro global del AppDbContext ya aplica pf_id (o catálogo global) y deleted_at;
        // acá se agrega que el catálogo solo expone tests activos, el filtro por capacidad
        // y el orden. El índice (pf_id, active) cubre esta combinación.
        await PersistenceErrors.ExecuteAsync(async () =>
        {
            var tests = context.CatalogTests
                .AsNoTracking()
                .Where(test => test.Active);

            if (motorCapacity is not null)
                tests = tests.Where(test => test.MotorCapacity == motorCapacity.Value);

            return await tests
                .OrderBy(test => test.Name)
                .ToListAsync(cancellationToken);
        });
}
