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
        {
            var candidates = await context.Athletes
                .Include(athlete => athlete.DigitalConsent)
                .Where(athlete => athlete.AccountId == accountId)
                .ToListAsync(cancellationToken);

            if (candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates[0];

            var candidateIds = candidates.Select(c => c.Id).ToList();
            var enrolledIds = await context.AthleteCategories
                .Where(ac => candidateIds.Contains(ac.AthleteId))
                .Select(ac => ac.AthleteId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var enrolled = candidates.FirstOrDefault(c => enrolledIds.Contains(c.Id) && c.Active)
                           ?? candidates.FirstOrDefault(c => enrolledIds.Contains(c.Id));

            return enrolled ?? candidates.FirstOrDefault(c => c.Active) ?? candidates[0];
        });

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
                    a.Active,
                    a.FirstName,
                    a.LastName
                })
                .ToListAsync(cancellationToken);

            if (athletes.Count == 0)
            {
                return Array.Empty<InternalAthleteSummaryDto>();
            }

            var athleteIds = athletes.Select(a => a.Id).ToList();

            var athleteCategories = await context.AthleteCategories
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(ac => athleteIds.Contains(ac.AthleteId) && ac.DeletedAt == null)
                .Join(
                    context.Categories.IgnoreQueryFilters().Where(c => c.DeletedAt == null),
                    ac => new { ac.CategoryId, ac.PfId },
                    c => new { CategoryId = c.Id, c.PfId },
                    (ac, c) => new { ac.AthleteId, ac.CategoryId, ac.CreatedAt }
                )
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.CategoryId)
                .ToListAsync(cancellationToken);

            var categoriesByAthlete = athleteCategories
                .GroupBy(x => x.AthleteId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<Guid>)g.Select(x => x.CategoryId).Distinct().ToList());

            return (IReadOnlyList<InternalAthleteSummaryDto>)athletes.Select(a =>
            {
                var catIds = categoriesByAthlete.TryGetValue(a.Id, out var list)
                    ? list
                    : Array.Empty<Guid>();

                return new InternalAthleteSummaryDto(
                    a.Id,
                    a.PfId,
                    catIds.Count > 0 ? catIds[0] : null,
                    a.Active,
                    catIds,
                    a.FirstName,
                    a.LastName,
                    $"{a.FirstName} {a.LastName}".Trim());
            }).ToList();
        });
}
