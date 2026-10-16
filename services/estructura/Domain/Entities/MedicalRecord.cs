using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

// Representa la ficha médica estructurada del atleta (antecedentes, cirugías, patologías y apto físico).
public sealed class MedicalRecord : BaseEntity
{
    public const int MaxTextLength = 2000;
    public const int MaxNotesLength = 1000;

    public Guid AthleteId { get; private set; }
    public string? MedicalHistory { get; private set; }
    public string? Surgeries { get; private set; }
    public string? Pathologies { get; private set; }
    public bool HasPhysicalFitnessCertificate { get; private set; }
    public DateOnly? PhysicalFitnessIssuedAt { get; private set; }
    public DateOnly? PhysicalFitnessExpiresAt { get; private set; }
    public string? PhysicalFitnessNotes { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private MedicalRecord() { }

    public static MedicalRecord Create(
        Guid athleteId,
        string? medicalHistory,
        string? surgeries,
        string? pathologies,
        bool hasPhysicalFitnessCertificate,
        DateOnly? physicalFitnessIssuedAt = null,
        DateOnly? physicalFitnessExpiresAt = null,
        string? physicalFitnessNotes = null)
    {
        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es inválido.");

        var record = new MedicalRecord
        {
            AthleteId = athleteId
        };

        record.ApplyAndValidate(
            medicalHistory,
            surgeries,
            pathologies,
            hasPhysicalFitnessCertificate,
            physicalFitnessIssuedAt,
            physicalFitnessExpiresAt,
            physicalFitnessNotes);

        return record;
    }

    public void Update(
        string? medicalHistory,
        string? surgeries,
        string? pathologies,
        bool hasPhysicalFitnessCertificate,
        DateOnly? physicalFitnessIssuedAt = null,
        DateOnly? physicalFitnessExpiresAt = null,
        string? physicalFitnessNotes = null)
    {
        ApplyAndValidate(
            medicalHistory,
            surgeries,
            pathologies,
            hasPhysicalFitnessCertificate,
            physicalFitnessIssuedAt,
            physicalFitnessExpiresAt,
            physicalFitnessNotes);
    }

    private void ApplyAndValidate(
        string? medicalHistory,
        string? surgeries,
        string? pathologies,
        bool hasPhysicalFitnessCertificate,
        DateOnly? physicalFitnessIssuedAt,
        DateOnly? physicalFitnessExpiresAt,
        string? physicalFitnessNotes)
    {
        var cleanHistory = medicalHistory?.Trim();
        if (cleanHistory?.Length > MaxTextLength)
            throw new DomainException($"Los antecedentes médicos no pueden superar los {MaxTextLength} caracteres.");

        var cleanSurgeries = surgeries?.Trim();
        if (cleanSurgeries?.Length > MaxTextLength)
            throw new DomainException($"Las cirugías no pueden superar los {MaxTextLength} caracteres.");

        var cleanPathologies = pathologies?.Trim();
        if (cleanPathologies?.Length > MaxTextLength)
            throw new DomainException($"Las patologías no pueden superar los {MaxTextLength} caracteres.");

        var cleanNotes = physicalFitnessNotes?.Trim();
        if (cleanNotes?.Length > MaxNotesLength)
            throw new DomainException($"Las observaciones del apto físico no pueden superar los {MaxNotesLength} caracteres.");

        if (physicalFitnessIssuedAt.HasValue && physicalFitnessExpiresAt.HasValue &&
            physicalFitnessExpiresAt.Value < physicalFitnessIssuedAt.Value)
        {
            throw new DomainException("La fecha de vencimiento del apto físico no puede ser anterior a la fecha de emisión.");
        }

        MedicalHistory = string.IsNullOrWhiteSpace(cleanHistory) ? null : cleanHistory;
        Surgeries = string.IsNullOrWhiteSpace(cleanSurgeries) ? null : cleanSurgeries;
        Pathologies = string.IsNullOrWhiteSpace(cleanPathologies) ? null : cleanPathologies;
        HasPhysicalFitnessCertificate = hasPhysicalFitnessCertificate;
        PhysicalFitnessIssuedAt = physicalFitnessIssuedAt;
        PhysicalFitnessExpiresAt = physicalFitnessExpiresAt;
        PhysicalFitnessNotes = string.IsNullOrWhiteSpace(cleanNotes) ? null : cleanNotes;
        UpdatedAt = DateTime.UtcNow;
    }
}
