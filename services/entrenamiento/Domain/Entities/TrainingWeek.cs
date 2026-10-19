using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class TrainingWeek : BaseEntity
{
    public int WeekNumber { get; private set; }
    public Guid TrainingPlanId { get; private set; }
    public TrainingPlan TrainingPlan { get; private set; } = null!;

    private readonly List<TrainingSession> _sessions = [];
    public IReadOnlyCollection<TrainingSession> Sessions => _sessions.AsReadOnly();

    private TrainingWeek(int weekNumber, Guid trainingPlanId)
    {
        WeekNumber = weekNumber;
        TrainingPlanId = trainingPlanId;
    }

    public static TrainingWeek Create(int weekNumber, Guid trainingPlanId)
    {
        if (weekNumber <= 0)
            throw new DomainException("El número de la semana debe ser mayor a cero.");

        return new TrainingWeek(weekNumber, trainingPlanId);
    }

    public void AddSession(TrainingSession session)
    {
        if (_sessions.Any(s => s.Order == session.Order))
            throw new DomainException($"La semana {WeekNumber} ya contiene una sesión con orden {session.Order}.");

        _sessions.Add(session);
    }

    public void RemoveSession(TrainingSession session)
    {
        _sessions.Remove(session);
    }
}