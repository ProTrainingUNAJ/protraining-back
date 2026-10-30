using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public class PerformedSet : BaseEntity
{
    public Guid AthleteId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid ExerciseId { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal Weight { get; private set; }
    public int Reps { get; private set; }

    public TrainingSession? Session { get; private set; }
    public Exercise? Exercise { get; private set; }

    private PerformedSet() { }

    public static PerformedSet Create(Guid athleteId, Guid sessionId, Guid exerciseId, DateOnly date, decimal weight, int reps)
    {
        if (athleteId == Guid.Empty)
            throw new DomainException("El atleta es obligatorio.");

        if (sessionId == Guid.Empty)
            throw new DomainException("La sesión es obligatoria.");

        if (exerciseId == Guid.Empty)
            throw new DomainException("El ejercicio es obligatorio.");

        if (weight <= 0)
            throw new DomainException("El peso debe ser mayor a 0 kg.");

        if (reps <= 0)
            throw new DomainException("Las repeticiones deben ser mayores a 0.");

        return new PerformedSet
        {
            AthleteId = athleteId,
            SessionId = sessionId,
            ExerciseId = exerciseId,
            Date = date,
            Weight = weight,
            Reps = reps
        };
    }
}

