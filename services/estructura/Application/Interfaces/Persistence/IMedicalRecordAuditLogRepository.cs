using Domain.Entities;

namespace Application.Interfaces.Persistence;

// Repositorio de solo inserción (append-only) y consulta para registros de auditoría médica.
public interface IMedicalRecordAuditLogRepository
{
    Task AddAsync(MedicalRecordAuditLog auditLog, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MedicalRecordAuditLog>> ListByAthleteIdAsync(Guid athleteId, CancellationToken cancellationToken = default);
}
