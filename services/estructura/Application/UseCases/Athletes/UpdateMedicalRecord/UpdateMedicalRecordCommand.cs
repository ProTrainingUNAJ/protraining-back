using Application.DTOs.Athletes;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.Athletes.UpdateMedicalRecord;

public sealed record UpdateMedicalRecordCommand(
    Guid AthleteId,
    string? Justification,
    string? MedicalHistory,
    string? Surgeries,
    string? Pathologies,
    bool HasPhysicalFitnessCertificate,
    DateOnly? PhysicalFitnessIssuedAt = null,
    DateOnly? PhysicalFitnessExpiresAt = null,
    string? PhysicalFitnessNotes = null);

public sealed class UpdateMedicalRecordCommandHandler(
    IAthleteRepository athleteRepository,
    IMedicalRecordAuditLogRepository auditLogRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public async Task<MedicalRecordDto> HandleAsync(
        UpdateMedicalRecordCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Justification))
            throw new DomainException("La justificación del acceso o modificación de la ficha médica es obligatoria.");

        var athlete = await athleteRepository.GetByIdAsync(command.AthleteId, cancellationToken)
            ?? throw new AthleteNotFoundException();

        var isUpdate = athlete.MedicalRecord is not null;

        var record = athlete.CompleteOrUpdateMedicalRecord(
            command.MedicalHistory,
            command.Surgeries,
            command.Pathologies,
            command.HasPhysicalFitnessCertificate,
            command.PhysicalFitnessIssuedAt,
            command.PhysicalFitnessExpiresAt,
            command.PhysicalFitnessNotes);

        var accessedByUserId = currentUser.UserId ?? currentUser.PfId
            ?? throw new UnauthorizedAccessException("Falta el identificador del usuario en el token.");

        var auditLog = isUpdate
            ? MedicalRecordAuditLog.CreateUpdate(athlete.Id, accessedByUserId, command.Justification)
            : MedicalRecordAuditLog.CreateCreation(athlete.Id, accessedByUserId, command.Justification);

        await auditLogRepository.AddAsync(auditLog, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AthleteMappings.ToMedicalRecordDto(athlete.Id, record);
    }
}
