using Application.DTOs.CheckIns;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class CheckInInjuryPainRepository(AppDbContext context) : ICheckInInjuryPainRepository
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<CheckInInjuryPainDto>>> ListByCheckInIdsAsync(
        IReadOnlyCollection<Guid> checkInIds,
        CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
        {
            if (checkInIds.Count == 0)
                return (IReadOnlyDictionary<Guid, IReadOnlyList<CheckInInjuryPainDto>>)new Dictionary<Guid, IReadOnlyList<CheckInInjuryPainDto>>();

            var rows = await (
                from pain in context.CheckInInjuryPains
                join injury in context.AthleteInjuries on pain.InjuryId equals injury.Id
                where checkInIds.Contains(pain.CheckInId)
                select new { pain.CheckInId, Dto = new CheckInInjuryPainDto(pain.InjuryId, injury.Zone, pain.Level) }
            ).AsNoTracking().ToListAsync(cancellationToken);

            var result = new Dictionary<Guid, List<CheckInInjuryPainDto>>();
            foreach (var row in rows)
            {
                if (!result.TryGetValue(row.CheckInId, out var list))
                {
                    list = new List<CheckInInjuryPainDto>();
                    result[row.CheckInId] = list;
                }
                list.Add(row.Dto);
            }

            return result.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<CheckInInjuryPainDto>)kvp.Value);
        });

    public async Task<IReadOnlyDictionary<Guid, int>> GetPeakPainByInjuryIdsAsync(
        IReadOnlyCollection<Guid> injuryIds,
        CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
        {
            if (injuryIds.Count == 0)
                return (IReadOnlyDictionary<Guid, int>)new Dictionary<Guid, int>();

            var rows = await context.CheckInInjuryPains
                .AsNoTracking()
                .Where(pain => injuryIds.Contains(pain.InjuryId))
                .GroupBy(pain => pain.InjuryId)
                .Select(group => new { InjuryId = group.Key, Peak = group.Max(p => p.Level) })
                .ToListAsync(cancellationToken);

            return rows.ToDictionary(row => row.InjuryId, row => row.Peak);
        });

    public async Task SetForCheckInAsync(
        Guid checkInId,
        IReadOnlyCollection<(Guid InjuryId, int Level)> pains,
        CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
        {
            var existing = await context.CheckInInjuryPains
                .Where(p => p.CheckInId == checkInId)
                .ToListAsync(cancellationToken);

            if (existing.Count > 0)
                context.CheckInInjuryPains.RemoveRange(existing);

            foreach (var (injuryId, level) in pains)
            {
                context.CheckInInjuryPains.Add(new CheckInInjuryPain
                {
                    CheckInId = checkInId,
                    InjuryId = injuryId,
                    Level = level
                });
            }
        });
}
