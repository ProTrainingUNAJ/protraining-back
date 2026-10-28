using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

// Lesión de un atleta con su ciclo de vida (KAN-419). Siempre nace en Active, con una
// fecha estimada de retorno obligatoria y posterior a la de la lesión. Los cambios de
// estado posteriores (KAN-420) dejan su fila en AthleteInjuryStatusChange.
public class AthleteInjury : BaseEntity
{
    public Guid AthleteId { get; set; }
    public string Zone { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime InjuryDate { get; set; }
    public DateTime EstimatedReturnDate { get; set; }
    public InjuryStatus Status { get; set; } = InjuryStatus.Active;
    public DateTime? ClearedAt { get; set; }

    // Las 19 zonas del check-in (KAN-385): los mismos ids del mapa de dolor del atleta.
    private static readonly IReadOnlySet<string> CheckInZones =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pecho", "hombros", "biceps", "abdominales", "cuadriceps",
            "espalda_alta", "espalda_media", "espalda_baja", "triceps", "gluteos",
            "isquiotibiales", "gemelos", "trapecios", "dorsales", "oblicuos",
            "antebrazos", "manos", "pies", "cuello_trapecios"
        };

    // Única puerta de entrada de una lesión nueva: sus reglas se rechazan como
    // DomainException → 400 con mensaje en español (ExceptionMiddleware).
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

        if (!CheckInZones.Contains(zone.Trim()))
            throw new DomainException(
                "La zona de la lesión no es válida: debe ser una de las 19 zonas del check-in (ej.: isquiotibiales).");

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

    // Fila inicial del historial que acompaña al alta (null → Active), con el autor del registro.
    public AthleteInjuryStatusChange InitialStatusChange(Guid changedBy, DateTime changedAt)
    {
        if (changedBy == Guid.Empty)
            throw new DomainException("El usuario que realiza el cambio es inválido.");

        return new AthleteInjuryStatusChange
        {
            InjuryId = Id,
            FromStatus = null,
            ToStatus = InjuryStatus.Active,
            Note = "Lesión registrada.",
            ChangedAt = changedAt,
            ChangedBy = changedBy
        };
    }

    // Únicas transiciones permitidas: la máquina de estados es estricta (KAN-420),
    // no se saltea la readaptación ni se vuelve atrás.
    private static readonly IReadOnlyDictionary<InjuryStatus, InjuryStatus> AllowedTransitions =
        new Dictionary<InjuryStatus, InjuryStatus>
        {
            [InjuryStatus.Active] = InjuryStatus.InRecovery,
            [InjuryStatus.InRecovery] = InjuryStatus.MedicallyCleared
        };

    // Cambia el estado de la lesión si la transición está permitida y devuelve la fila del
    // historial clínico que el caso de uso debe persistir. Toda violación de regla sale como
    // DomainException → 400 con mensaje en español (ExceptionMiddleware).
    public AthleteInjuryStatusChange TransitionTo(
        InjuryStatus newStatus,
        string? note,
        Guid changedBy,
        DateTime changedAt)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var next) || next != newStatus)
        {
            // Regla médica rígida del CA1: prohibido dar de alta una lesión activa sin
            // haber pasado antes por el periodo de readaptación (En recuperación).
            if (Status == InjuryStatus.Active && newStatus == InjuryStatus.MedicallyCleared)
                throw new DomainException(
                    "Una lesión activa no puede pasar directamente a 'Alta médica'. Debe pasar primero por 'En recuperación'.");

            if (newStatus == Status)
                throw new DomainException($"La lesión ya está en el estado '{Label(Status)}'.");

            throw new DomainException(
                $"Transición de estado no permitida: de '{Label(Status)}' a '{Label(newStatus)}'.");
        }

        if (string.IsNullOrWhiteSpace(note))
            throw new DomainException("La nota clínica es obligatoria.");

        if (changedBy == Guid.Empty)
            throw new DomainException("El usuario que realiza el cambio es inválido.");

        var previousStatus = Status;
        Status = newStatus;

        // Alta médica: se libera al atleta, quedando asentado cuándo ocurrió.
        if (newStatus == InjuryStatus.MedicallyCleared)
            ClearedAt = changedAt;

        return new AthleteInjuryStatusChange
        {
            InjuryId = Id,
            FromStatus = previousStatus,
            ToStatus = newStatus,
            Note = note.Trim(),
            ChangedAt = changedAt,
            ChangedBy = changedBy
        };
    }

    // Etiquetas en español de los estados, para los mensajes que lee una persona.
    private static string Label(InjuryStatus status) => status switch
    {
        InjuryStatus.Active => "Activa",
        InjuryStatus.InRecovery => "En recuperación",
        InjuryStatus.MedicallyCleared => "Alta médica",
        _ => status.ToString()
    };
}

// Ciclo de vida de una lesión. La máquina de estados y sus reglas son la KAN-420.
public enum InjuryStatus
{
    Active,
    InRecovery,
    MedicallyCleared
}
