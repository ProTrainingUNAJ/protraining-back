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

    public IReadOnlyCollection<TrainingBlock> Blocks => _blocks.AsReadOnly();
    private readonly List<TrainingBlock> _blocks = new();

    public IReadOnlyCollection<PrescribedExercise> PrescribedExercises =>
        _prescribedExercises.Count > 0
            ? _prescribedExercises.AsReadOnly()
            : _blocks.OrderBy(b => b.Order).SelectMany(b => b.PrescribedExercises.OrderBy(pe => pe.Order)).ToList().AsReadOnly();
    private readonly List<PrescribedExercise> _prescribedExercises = new();

    private const string LegacyBlockMetaPrefix = "<!--PT_BLOCK_META:";

    private TrainingSession(Guid trainingWeekId, string name, int order, DayOfWeek dayOfWeek, string? coachNotes, TimeOnly? startTime)
    {
        TrainingWeekId = trainingWeekId;
        Name = name;
        Order = order;
        DayOfWeek = dayOfWeek;
        CoachNotes = SanitizeCoachNotes(coachNotes);
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
        CoachNotes = SanitizeCoachNotes(notes);
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
        CoachNotes = SanitizeCoachNotes(coachNotes);
        StartTime = startTime;
    }

    private static string? SanitizeCoachNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;

        var metaIdx = notes.IndexOf(LegacyBlockMetaPrefix, StringComparison.Ordinal);
        var cleaned = metaIdx >= 0 ? notes[..metaIdx].Trim() : notes.Trim();

        if (string.IsNullOrEmpty(cleaned))
            return null;

        return cleaned.Length > 1000 ? cleaned[..1000] : cleaned;
    }

    public void SetTemporaryOrder(int temporaryOrder)
    {
        Order = temporaryOrder;
    }

    public void AddBlock(TrainingBlock block)
    {
        if (_blocks.Any(b => b.Order == block.Order))
            throw new DomainException($"La sesión '{Name}' ya contiene un bloque con orden {block.Order}.");

        _blocks.Add(block);

        foreach (var pe in block.PrescribedExercises)
        {
            if (!_prescribedExercises.Contains(pe))
            {
                pe.SetTrainingSessionId(Id);
                _prescribedExercises.Add(pe);
            }
        }
    }

    public void RemoveBlock(TrainingBlock block)
    {
        _blocks.Remove(block);
        foreach (var pe in block.PrescribedExercises)
        {
            _prescribedExercises.Remove(pe);
        }
    }

    public void ClearBlocks()
    {
        _blocks.Clear();
        _prescribedExercises.Clear();
    }

    public void AddPrescribedExercise(PrescribedExercise exercise)
    {
        if (_prescribedExercises.Any(e => e.Order == exercise.Order))
            throw new DomainException($"La sesión '{Name}' ya contiene un ejercicio con orden {exercise.Order}.");

        var defaultBlock = _blocks.FirstOrDefault(b => b.Title == "Principal") ?? _blocks.FirstOrDefault();
        if (defaultBlock == null)
        {
            defaultBlock = TrainingBlock.Create(Id, "Principal", 1);
            _blocks.Add(defaultBlock);
        }

        exercise.SetTrainingBlockId(defaultBlock.Id);
        exercise.SetTrainingSessionId(Id);

        if (!defaultBlock.PrescribedExercises.Contains(exercise))
        {
            defaultBlock.AddPrescribedExercise(exercise);
        }

        _prescribedExercises.Add(exercise);
    }

    public void RemovePrescribedExercise(PrescribedExercise exercise)
    {
        _prescribedExercises.Remove(exercise);
        foreach (var block in _blocks)
        {
            block.RemovePrescribedExercise(exercise);
        }
    }

    /// <summary>
    /// Consolida en esta sesión (sesión primaria del día) los bloques de todos los entrenamientos del día (KAN-587).
    /// Asegura orden correlativo sin colisiones y conserva la información de horario y notas técnicas.
    /// </summary>
    public void ConsolidateWithDaySessions(IReadOnlyList<TrainingSession> daySessions)
    {
        if (daySessions == null || daySessions.Count <= 1)
            return;

        var allBlocks = daySessions
            .OrderBy(s => s.Order)
            .SelectMany(s => s.Blocks.OrderBy(b => b.Order))
            .ToList();

        _blocks.Clear();
        _prescribedExercises.Clear();

        int seqOrder = 1;
        int exOrder = 1;
        foreach (var b in allBlocks)
        {
            b.Update(b.Title, seqOrder++);
            _blocks.Add(b);
            foreach (var pe in b.PrescribedExercises.OrderBy(e => e.Order))
            {
                pe.UpdateOrder(exOrder++);
                pe.SetTrainingSessionId(Id);
                _prescribedExercises.Add(pe);
            }
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
