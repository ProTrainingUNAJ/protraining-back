using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class PendingPurge : BaseEntity
{
    public const string PendingStatus = "Pending";
    public const string ProcessedStatus = "Processed";

    public Guid AthleteId { get; private set; }
    public string EntityType { get; private set; } = "Athlete";
    public string Status { get; private set; } = PendingStatus;
    public DateTime? ProcessedAt { get; private set; }

    private PendingPurge() { }

    public static PendingPurge Create(Guid athleteId, string entityType = "Athlete")
    {
        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es inválido.");

        var cleanType = entityType?.Trim();
        if (string.IsNullOrWhiteSpace(cleanType) || cleanType.Length > 50)
            throw new DomainException("El tipo de entidad es inválido.");

        return new PendingPurge
        {
            AthleteId = athleteId,
            EntityType = cleanType,
            Status = PendingStatus
        };
    }

    public void MarkAsProcessed()
    {
        Status = ProcessedStatus;
        ProcessedAt = DateTime.UtcNow;
    }
}
