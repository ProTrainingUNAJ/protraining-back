using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

// Lesión de un atleta con su ciclo de vida (KAN-419). Siempre nace en Active, con una
// fecha estimada de retorno obligatoria y posterior a la de la lesión. Los cambios de
// estado posteriores (KAN-420) dejan su fila en AthleteInjuryStatusChange.
// Zone es texto libre obligatorio con una de las 19 zonas del check-in (KAN-385),
// por ejemplo "isquiotibiales".
public class AthleteInjury : BaseEntity
{
    public Guid AthleteId { get; set; }
    public string Zone { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime InjuryDate { get; set; }
    public DateTime EstimatedReturnDate { get; set; }
    public InjuryStatus Status { get; set; } = InjuryStatus.Active;
    public DateTime? ClearedAt { get; set; }

    // Única puerta de entrada de una lesión nueva: aplica las reglas de negocio del dominio
    // (DomainException → 400 con mensaje en español vía ExceptionMiddleware).
    public static AthleteInjury Create(
        Guid athleteId,
        string zone,
        string description,
        DateTime injuryDate,
        DateTime? estimatedReturnDate)
    {
        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es inválido.");

        if (string.IsNullOrWhiteSpace(zone))
            throw new DomainException("La zona de la lesión es obligatoria.");

        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("La descripción de la lesión es obligatoria.");

        if (estimatedReturnDate is null)
            throw new DomainException("La fecha de retorno estimada es obligatoria.");

        // Solo importa el día: ambas fechas se guardan sin hora (columna date).
        if (estimatedReturnDate.Value.Date <= injuryDate.Date)
            throw new DomainException("La fecha de retorno estimada debe ser posterior a la fecha de la lesión.");

        return new AthleteInjury
        {
            AthleteId = athleteId,
            Zone = zone.Trim(),
            Description = description.Trim(),
            InjuryDate = injuryDate.Date,
            EstimatedReturnDate = estimatedReturnDate.Value.Date,
            Status = InjuryStatus.Active
        };
    }
}

// Ciclo de vida de una lesión. La máquina de estados y sus reglas son la KAN-420.
public enum InjuryStatus
{
    Active,
    InRecovery,
    MedicallyCleared
}
