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

    // Hora de inicio opcional, en hora Argentina (UTC-3). Sin hora, la sesión vale para todo el día.
    public TimeOnly? StartTime { get; private set; }

    public IReadOnlyCollection<PrescribedExercise> PrescribedExercises => _prescribedExercises.AsReadOnly();
    private readonly List<PrescribedExercise> _prescribedExercises = new();

    private TrainingSession(Guid trainingWeekId, string name, int order, DayOfWeek dayOfWeek, string? coachNotes, TimeOnly? startTime)
    {
        TrainingWeekId = trainingWeekId;
        Name = name;
        Order = order;
        DayOfWeek = dayOfWeek;
        CoachNotes = coachNotes;
        StartTime = startTime;
    }

    public static TrainingSession Create(
        Guid trainingWeekId,
        string name,
        int order,
        DayOfWeek dayOfWeek,
        string? coachNotes = null,
        TimeOnly? startTime = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre de la sesión no puede estar vacío.");

        if (order <= 0)
            throw new DomainException("El orden debe ser mayor a cero.");

        return new TrainingSession(trainingWeekId, name, order, dayOfWeek, coachNotes, startTime);
    }

    public void UpdateCoachNotes(string? notes)
    {
        CoachNotes = notes;
    }

    public void Update(string name, int order, DayOfWeek dayOfWeek, string? coachNotes, TimeOnly? startTime = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre de la sesión no puede estar vacío.");

        if (order <= 0)
            throw new DomainException("El orden debe ser mayor a cero.");

        Name = name;
        Order = order;
        DayOfWeek = dayOfWeek;
        CoachNotes = coachNotes;
        StartTime = startTime;
    }

    public void SetTemporaryOrder(int temporaryOrder)
    {
        Order = temporaryOrder;
    }

    public void AddPrescribedExercise(PrescribedExercise exercise)
    {
        if (_prescribedExercises.Any(e => e.Order == exercise.Order))
            throw new DomainException($"La sesión '{Name}' ya contiene un ejercicio con orden {exercise.Order}.");

        _prescribedExercises.Add(exercise);
    }

    public void RemovePrescribedExercise(PrescribedExercise exercise)
    {
        _prescribedExercises.Remove(exercise);
    }

    /// <summary>
    /// Consolida en esta sesión (sesión primaria del día) los ejercicios prescritos de todos los bloques del día (KAN-587).
    /// Asegura orden correlativo sin colisiones y conserva la información de horario y notas técnicas.
    /// </summary>
    public void ConsolidateWithDaySessions(IReadOnlyList<TrainingSession> daySessions)
    {
        if (daySessions == null || daySessions.Count <= 1)
            return;

        var allPrescribed = daySessions
            .OrderBy(s => s.Order)
            .SelectMany(s => s.PrescribedExercises.OrderBy(pe => pe.Order))
            .ToList();

        _prescribedExercises.Clear();
        int seqOrder = 1;
        foreach (var pe in allPrescribed)
        {
            pe.UpdateOrder(seqOrder++);
            _prescribedExercises.Add(pe);
        }

        if (StartTime == null)
        {
            var sessionWithTime = daySessions.FirstOrDefault(s => s.StartTime != null);
            if (sessionWithTime != null)
            {
                StartTime = sessionWithTime.StartTime;
            }
        }

        if (string.IsNullOrWhiteSpace(CoachNotes))
        {
            var sessionWithNotes = daySessions.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.CoachNotes));
            if (sessionWithNotes != null)
            {
                CoachNotes = sessionWithNotes.CoachNotes;
            }
        }
    }
}
