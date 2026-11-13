using Domain.Common;

namespace Domain.Entities;

// Historial clínico de una lesión: cada cambio de estado deja una fila (la muestra la
// KAN-421 y la escribe la máquina de estados de la KAN-420). KAN-419 solo crea la tabla.
public class AthleteInjuryStatusChange : BaseEntity
{
    public Guid InjuryId { get; set; }

    // Anulable: el alta puede registrarse como null → Active.
    public InjuryStatus? FromStatus { get; set; }

    public InjuryStatus ToStatus { get; set; }

    // Observación clínica del cambio.
    public string Note { get; set; } = string.Empty;

    public DateTime ChangedAt { get; set; }

    // Quién hizo el cambio: el claim pf_id del token (la identidad que expone ICurrentUser).
    public Guid ChangedBy { get; set; }
}
