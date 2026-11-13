using Application.DTOs.Athletes;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class AthleteRepository(AppDbContext context) : IAthleteRepository
{
    public async Task<IReadOnlyList<Athlete>> ListAsync(CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.Athletes
                .AsNoTracking()
                .Include(athlete => athlete.DigitalConsent)
                .OrderBy(athlete => athlete.LastName)
                .ThenBy(athlete => athlete.FirstName)
                .ToListAsync(cancellationToken));

    public async Task<Athlete?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.Athletes
                .Include(athlete => athlete.DigitalConsent)
                .FirstOrDefaultAsync(athlete => athlete.Id == id, cancellationToken));

    public async Task<Athlete?> GetByIdWithMedicalRecordAsync(Guid id, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.Athletes
                .Include(athlete => athlete.DigitalConsent)
                .Include(athlete => athlete.MedicalRecord)
                .FirstOrDefaultAsync(athlete => athlete.Id == id, cancellationToken));

    public async Task<Athlete?> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.Athletes
                .Include(athlete => athlete.DigitalConsent)
                .FirstOrDefaultAsync(athlete => athlete.AccountId == accountId, cancellationToken));

    public Task AddAsync(Athlete athlete, CancellationToken cancellationToken = default)
    {
        context.Athletes.Add(athlete);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Athlete athlete, CancellationToken cancellationToken = default)
    {
        context.Athletes.Remove(athlete);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Guid>> GetDistinctPfIdsAsync(CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.Athletes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Select(a => a.PfId)
                .Distinct()
                .ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<InternalAthleteSummaryDto>> GetActiveAthletesInternalAsync(CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
        {
            var athletes = await context.Athletes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(a => a.Active && a.DeletedAt == null)
                .Select(a => new
                {
                    a.Id,
                    a.PfId,
                    a.Active
                })
                .ToListAsync(cancellationToken);

            if (athletes.Count == 0)
            {
                return Array.Empty<InternalAthleteSummaryDto>();
            }

            var athleteIds = athletes.Select(a => a.Id).ToList();

            var categoriesByAthlete = await context.AthleteCategories
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(ac => athleteIds.Contains(ac.AthleteId) && ac.DeletedAt == null)
                .GroupBy(ac => ac.AthleteId)
                .Select(g => new { AthleteId = g.Key, CategoryId = g.Select(x => x.CategoryId).FirstOrDefault() })
                .ToDictionaryAsync(x => x.AthleteId, x => (Guid?)x.CategoryId, cancellationToken);

            return (IReadOnlyList<InternalAthleteSummaryDto>)athletes.Select(a => new InternalAthleteSummaryDto(
                a.Id,
                a.PfId,
                categoriesByAthlete.TryGetValue(a.Id, out var catId) ? catId : null,
                a.Active
            )).ToList();
        });
}
