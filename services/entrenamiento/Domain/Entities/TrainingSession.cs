using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public class TrainingSession : BaseEntity
{
    public Guid TrainingWeekId { get; private set; }
    public TrainingWeek Week { get; private set; } = null!;

    public string Name { get; private set; }
    public int Order { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public string? CoachNotes { get; private set; }

    public IReadOnlyCollection<PrescribedExercise> PrescribedExercises => _prescribedExercises.AsReadOnly();
    private readonly List<PrescribedExercise> _prescribedExercises = new();

    private TrainingSession(Guid trainingWeekId, string name, int order, DayOfWeek dayOfWeek, string? coachNotes)
    {
        TrainingWeekId = trainingWeekId;
        Name = name;
        Order = order;
        DayOfWeek = dayOfWeek;
        CoachNotes = coachNotes;
    }

    public static TrainingSession Create(Guid trainingWeekId, string name, int order, DayOfWeek dayOfWeek, string? coachNotes = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre de la sesión no puede estar vacío.");

        if (order <= 0)
            throw new DomainException("El orden debe ser mayor a cero.");

        return new TrainingSession(trainingWeekId, name, order, dayOfWeek, coachNotes);
    }

    public void UpdateCoachNotes(string? notes)
    {
        CoachNotes = notes;
    }

    public void AddPrescribedExercise(PrescribedExercise exercise)
    {
        if (_prescribedExercises.Any(e => e.Order == exercise.Order))
            throw new DomainException($"La sesión '{Name}' ya contiene un ejercicio con orden {exercise.Order}.");

        _prescribedExercises.Add(exercise);
    }
}
