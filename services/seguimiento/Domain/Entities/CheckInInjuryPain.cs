using Domain.Common;

namespace Domain.Entities;

// Puntaje diario de dolor (0 a 10) de una molestia activa del atleta en su check-in del día.
// Se guarda por molestia y por check-in para que la regla «el mismo dolor toda la semana»
// pueda seguir la evolución de cada una de forma independiente.
public class CheckInInjuryPain : BaseEntity
{
    public Guid CheckInId { get; set; }

    public Guid InjuryId { get; set; }

    public int Level { get; set; }
}
