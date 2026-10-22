using Application.DTOs.Athletes;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Athletes.ListMedicalRecordAuditLogs;

public sealed record ListMedicalRecordAuditLogsQuery(Guid AthleteId);

public sealed class ListMedicalRecordAuditLogsQueryHandler(
    IAthleteRepository athleteRepository,
    IMedicalRecordAuditLogRepository auditLogRepository)
{
    public async Task<IReadOnlyList<MedicalRecordAuditLogDto>> HandleAsync(
        ListMedicalRecordAuditLogsQuery query,
        CancellationToken cancellationToken = default)
    {
        var athlete = await athleteRepository.GetByIdAsync(query.AthleteId, cancellationToken)
            ?? throw new AthleteNotFoundException();

        var logs = await auditLogRepository.ListByAthleteIdAsync(athlete.Id, cancellationToken);
        return logs.Select(AthleteMappings.ToAuditLogDto).ToList();
    }
}
