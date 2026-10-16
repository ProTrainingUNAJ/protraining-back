using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class MedicalRecordAuditLogRepository(AppDbContext context) : IMedicalRecordAuditLogRepository
{
    public Task AddAsync(MedicalRecordAuditLog auditLog, CancellationToken cancellationToken = default)
    {
        context.MedicalRecordAuditLogs.Add(auditLog);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<MedicalRecordAuditLog>> ListByAthleteIdAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.MedicalRecordAuditLogs
                .AsNoTracking()
                .Where(log => log.AthleteId == athleteId)
                .OrderByDescending(log => log.CreatedAt)
                .ToListAsync(cancellationToken));
}
