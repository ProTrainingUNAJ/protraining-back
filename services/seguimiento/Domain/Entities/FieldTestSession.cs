using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

// Sesión de testeo de una categoría (KAN-424): agrupa los tests del catálogo que entran
// en la evaluación (p. ej. "Evaluación de pretemporada") y los resultados de cada atleta.
public class FieldTestSession : BaseEntity
{
    public Guid CategoryId { get; set; }
    public DateTime Date { get; set; }
    public string Name { get; set; } = string.Empty;
    public FieldTestSessionStatus Status { get; set; } = FieldTestSessionStatus.Planned;

    // Únicas transiciones permitidas (KAN-568): estricta y secuencial,
    // solo Planificada → En curso → Cerrada, sin saltos ni reaperturas.
    private static readonly IReadOnlyDictionary<FieldTestSessionStatus, FieldTestSessionStatus> AllowedTransitions =
        new Dictionary<FieldTestSessionStatus, FieldTestSessionStatus>
        {
            [FieldTestSessionStatus.Planned] = FieldTestSessionStatus.InProgress,
            [FieldTestSessionStatus.InProgress] = FieldTestSessionStatus.Closed
        };

    // Cambia el estado si la transición está permitida. Toda violación sale como
    // DomainException → 400 (ExceptionMiddleware) con mensaje que lee una persona.
    public void TransitionTo(FieldTestSessionStatus newStatus)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var next) || next != newStatus)
        {
            if (newStatus == Status)
                throw new DomainException($"La sesión ya está en el estado '{Label(Status)}'.");

            if (Status == FieldTestSessionStatus.Closed)
                throw new DomainException("La sesión está cerrada: no se puede cambiar su estado.");

            throw new DomainException(
                $"Transición de estado no permitida: de '{Label(Status)}' a '{Label(newStatus)}'. " +
                "Orden válido: 'Planificada' → 'En curso' → 'Cerrada'.");
        }

        Status = newStatus;
    }

    // Etiquetas en español de los estados, para los mensajes que lee una persona.
    private static string Label(FieldTestSessionStatus status) => status switch
    {
        FieldTestSessionStatus.Planned => "Planificada",
        FieldTestSessionStatus.InProgress => "En curso",
        FieldTestSessionStatus.Closed => "Cerrada",
        _ => status.ToString()
    };
}
