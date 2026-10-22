using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class PrescribedExercise : BaseEntity
{
    public int Order { get; private set; }
    public int Sets { get; private set; }
    public int Reps { get; private set; }
    public decimal? TargetPercentage { get; private set; }

    public Guid TrainingSessionId { get; private set; }
    public TrainingSession TrainingSession { get; private set; } = null!;

    public Guid ExerciseId { get; private set; }
    public Exercise Exercise { get; private set; } = null!;

    private PrescribedExercise(int order, int sets, int reps, decimal? targetPercentage, Guid trainingSessionId, Guid exerciseId)
    {
        Order = order;
        Sets = sets;
        Reps = reps;
        TargetPercentage = targetPercentage;
        TrainingSessionId = trainingSessionId;
        ExerciseId = exerciseId;
    }

    public static PrescribedExercise Create(int order, int sets, int reps, decimal? targetPercentage, Guid trainingSessionId, Guid exerciseId)
    {
        if (order <= 0)
            throw new DomainException("El orden debe ser mayor a cero.");

        if (sets <= 0 || reps <= 0)
            throw new DomainException("Las series y repeticiones deben ser mayores a cero.");

        return new PrescribedExercise(order, sets, reps, targetPercentage, trainingSessionId, exerciseId);
    }

    public void Update(int order, int sets, int reps, decimal? targetPercentage, Guid exerciseId)
    {
        if (order <= 0)
            throw new DomainException("El orden debe ser mayor a cero.");

        if (sets <= 0 || reps <= 0)
            throw new DomainException("Las series y repeticiones deben ser mayores a cero.");

        Order = order;
        Sets = sets;
        Reps = reps;
        TargetPercentage = targetPercentage;
        ExerciseId = exerciseId;
    }
}