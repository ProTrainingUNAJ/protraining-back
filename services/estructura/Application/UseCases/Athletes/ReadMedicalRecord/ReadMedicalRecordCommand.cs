using Application.DTOs.Athletes;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.Athletes.ReadMedicalRecord;

public sealed record ReadMedicalRecordCommand(
    Guid AthleteId,
    string? Justification);

public sealed class ReadMedicalRecordCommandHandler(
    IAthleteRepository athleteRepository,
    IMedicalRecordAuditLogRepository auditLogRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public async Task<MedicalRecordDto> HandleAsync(
        ReadMedicalRecordCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Justification))
            throw new DomainException("La justificación del acceso o modificación de la ficha médica es obligatoria.");

        var athlete = await athleteRepository.GetByIdAsync(command.AthleteId, cancellationToken)
            ?? throw new AthleteNotFoundException();

        var record = athlete.ReadMedicalRecord();

        var accessedByUserId = currentUser.UserId ?? currentUser.PfId
            ?? throw new UnauthorizedAccessException("Falta el identificador del usuario en el token.");

        var auditLog = MedicalRecordAuditLog.CreateRead(
            athlete.Id,
            accessedByUserId,
            command.Justification);

        await auditLogRepository.AddAsync(auditLog, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AthleteMappings.ToMedicalRecordDto(athlete.Id, record);
    }
}
