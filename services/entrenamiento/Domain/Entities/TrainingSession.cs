using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class TrainingSession : BaseEntity
{
    public string Name { get; private set; }
    public int Order { get; private set; }
    public Guid TrainingWeekId { get; private set; }
    public TrainingWeek TrainingWeek { get; private set; } = null!;

    private readonly List<PrescribedExercise> _prescribedExercises = [];
    public IReadOnlyCollection<PrescribedExercise> PrescribedExercises => _prescribedExercises.AsReadOnly();

    private TrainingSession(string name, int order, Guid trainingWeekId)
    {
        Name = name;
        Order = order;
        TrainingWeekId = trainingWeekId;
    }

    public static TrainingSession Create(string name, int order, Guid trainingWeekId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre de la sesión no puede estar vacío.");

        return new TrainingSession(name, order, trainingWeekId);
    }
}