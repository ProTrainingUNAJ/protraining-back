using Domain.Exceptions;

namespace Domain.Entities;

public sealed class ExerciseSubstitute
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RestrictionId { get; private set; }
    public ExerciseRestriction Restriction { get; private set; } = null!;
    public Guid SubstituteExerciseId { get; private set; }
    public Exercise SubstituteExercise { get; internal set; } = null!;
    public int Order { get; private set; }

    private ExerciseSubstitute() { }

    private ExerciseSubstitute(Guid restrictionId, Guid substituteExerciseId, int order)
    {
        RestrictionId = restrictionId;
        SubstituteExerciseId = substituteExerciseId;
        Order = order;
    }

    public static ExerciseSubstitute Create(Guid restrictionId, Guid substituteExerciseId, int order)
    {
        if (restrictionId == Guid.Empty)
            throw new DomainException("La restricción es obligatoria.");

        if (substituteExerciseId == Guid.Empty)
            throw new DomainException("El ejercicio sustituto es obligatorio.");

        if (order <= 0)
            throw new DomainException("El orden del sustituto debe ser mayor a cero.");

        return new ExerciseSubstitute(restrictionId, substituteExerciseId, order);
    }

    public static ExerciseSubstitute Create(ExerciseRestriction restriction, Guid substituteExerciseId, int order)
    {
        if (restriction == null)
            throw new DomainException("La restricción es obligatoria.");

        if (restriction.ExerciseId == substituteExerciseId)
            throw new DomainException("El ejercicio sustituto no puede ser igual al ejercicio restringido.");

        return Create(restriction.Id, substituteExerciseId, order);
    }
}
