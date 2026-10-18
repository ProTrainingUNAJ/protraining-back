using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

// Registro inmutable (append-only) de auditoría legal por cada lectura o alteración de una ficha médica.
public sealed class MedicalRecordAuditLog : BaseEntity
{
    public const string ReadAction = "Read";
    public const string CreateAction = "Create";
    public const string UpdateAction = "Update";

    public const int MaxActionLength = 50;
    public const int MaxJustificationLength = 500;

    public Guid AthleteId { get; private set; }
    public Guid AccessedByUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string Justification { get; private set; } = string.Empty;

    private MedicalRecordAuditLog() { }

    public static MedicalRecordAuditLog Create(
        Guid athleteId,
        Guid accessedByUserId,
        string action,
        string justification)
    {
        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es inválido.");

        if (accessedByUserId == Guid.Empty)
            throw new DomainException("El identificador del usuario que realiza el acceso médico es inválido.");

        var cleanAction = action?.Trim();
        if (string.IsNullOrWhiteSpace(cleanAction) ||
            (cleanAction != ReadAction && cleanAction != CreateAction && cleanAction != UpdateAction))
        {
            throw new DomainException("La acción de auditoría médica es inválida. Debe ser Read, Create o Update.");
        }

        var cleanJustification = justification?.Trim();
        if (string.IsNullOrWhiteSpace(cleanJustification))
            throw new DomainException("La justificación del acceso o modificación de la ficha médica es obligatoria.");

        if (cleanJustification.Length > MaxJustificationLength)
            throw new DomainException($"La justificación no puede superar los {MaxJustificationLength} caracteres.");

        return new MedicalRecordAuditLog
        {
            AthleteId = athleteId,
            AccessedByUserId = accessedByUserId,
            Action = cleanAction,
            Justification = cleanJustification
        };
    }

    public static MedicalRecordAuditLog CreateRead(
        Guid athleteId,
        Guid accessedByUserId,
        string justification) =>
        Create(athleteId, accessedByUserId, ReadAction, justification);

    public static MedicalRecordAuditLog CreateCreation(
        Guid athleteId,
        Guid accessedByUserId,
        string justification) =>
        Create(athleteId, accessedByUserId, CreateAction, justification);

    public static MedicalRecordAuditLog CreateUpdate(
        Guid athleteId,
        Guid accessedByUserId,
        string justification) =>
        Create(athleteId, accessedByUserId, UpdateAction, justification);
}
