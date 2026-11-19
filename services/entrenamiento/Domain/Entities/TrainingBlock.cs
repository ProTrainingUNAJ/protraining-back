using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public class TrainingBlock : BaseEntity
{
    public Guid TrainingSessionId { get; private set; }
    public TrainingSession TrainingSession { get; private set; } = null!;

    public string Title { get; private set; }
    public int Order { get; private set; }

    public IReadOnlyCollection<PrescribedExercise> PrescribedExercises => _prescribedExercises.AsReadOnly();
    private readonly List<PrescribedExercise> _prescribedExercises = new();

    private TrainingBlock(Guid trainingSessionId, string title, int order)
    {
        TrainingSessionId = trainingSessionId;
        Title = title;
        Order = order;
    }

    public static TrainingBlock Create(Guid trainingSessionId, string title, int order)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("El título del bloque no puede estar vacío.");

        if (order <= 0)
            throw new DomainException("El orden debe ser mayor a cero.");

        return new TrainingBlock(trainingSessionId, title.Trim(), order);
    }

    public void Update(string title, int order)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("El título del bloque no puede estar vacío.");

        if (order <= 0)
            throw new DomainException("El orden debe ser mayor a cero.");

        Title = title.Trim();
        Order = order;
    }

    public void SetTemporaryOrder(int temporaryOrder)
    {
        Order = temporaryOrder;
    }

    public void AddPrescribedExercise(PrescribedExercise exercise)
    {
        if (_prescribedExercises.Any(e => e.Order == exercise.Order))
            throw new DomainException($"El bloque '{Title}' ya contiene un ejercicio con orden {exercise.Order}.");

        _prescribedExercises.Add(exercise);
    }

    public void RemovePrescribedExercise(PrescribedExercise exercise)
    {
        _prescribedExercises.Remove(exercise);
    }

    public void ClearExercises()
    {
        _prescribedExercises.Clear();
    }
}
