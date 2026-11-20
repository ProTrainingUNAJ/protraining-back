using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class PendingPurge : BaseEntity
{
    public const string PendingStatus = "Pending";
    public const string ProcessedStatus = "Processed";

    public Guid AthleteId { get; private set; }
    public Guid? AccountId { get; private set; }
    public string EntityType { get; private set; } = "Athlete";
    public string Status { get; private set; } = PendingStatus;
    public DateTime? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    private PendingPurge() { }

    // accountId es obligatorio (aunque puede ser null si el atleta nunca tuvo cuenta): así nadie omite Identidad sin querer.
    public static PendingPurge Create(Guid athleteId, Guid? accountId, string entityType = "Athlete")
    {
        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es inválido.");

        var cleanType = entityType?.Trim();
        if (string.IsNullOrWhiteSpace(cleanType) || cleanType.Length > 50)
            throw new DomainException("El tipo de entidad es inválido.");

        return new PendingPurge
        {
            AthleteId = athleteId,
            AccountId = accountId,
            EntityType = cleanType,
            Status = PendingStatus
        };
    }

    public void MarkAsProcessed()
    {
        Status = ProcessedStatus;
        ProcessedAt = DateTime.UtcNow;
    }

    public void RecordFailure(string error)
    {
        Attempts++;
        var cleanError = error?.Trim();
        LastError = cleanError?.Length > 500 ? cleanError[..500] : cleanError;
    }
}
