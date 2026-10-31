using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public class TrainingPlan : BaseEntity
{
    public string Name { get; private set; }
    public string? Objective { get; private set; }
    public bool IsTemplate { get; private set; }

    public IReadOnlyCollection<TrainingWeek> Weeks => _weeks.AsReadOnly();
    private readonly List<TrainingWeek> _weeks = new();

    public IReadOnlyCollection<PlanAssignment> Assignments => _assignments.AsReadOnly();
    private readonly List<PlanAssignment> _assignments = new();

    private TrainingPlan(string name, string? objective, bool isTemplate)
    {
        Name = name;
        Objective = objective;
        IsTemplate = isTemplate;
    }

    public static TrainingPlan Create(string name, string? objective = null, bool isTemplate = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del plan no puede estar vacío.");

        return new TrainingPlan(name, objective, isTemplate);
    }

    public void Update(string name, string? objective)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del plan no puede estar vacío.");

        Name = name;
        Objective = objective;
    }

    public void AddWeek(TrainingWeek week)
    {
        if (_weeks.Any(w => w.WeekNumber == week.WeekNumber))
            throw new DomainException($"El plan ya contiene una semana con número {week.WeekNumber}.");

        _weeks.Add(week);
    }

    public void RemoveWeek(TrainingWeek week)
    {
        _weeks.Remove(week);
    }

    public void ClearWeeks()
    {
        _weeks.Clear();
    }
}
