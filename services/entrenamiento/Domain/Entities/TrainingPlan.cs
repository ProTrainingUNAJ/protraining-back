using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public class TrainingPlan : BaseEntity
{
    public string Name { get; private set; }
    public string? Objective { get; private set; }

    public IReadOnlyCollection<TrainingWeek> Weeks => _weeks.AsReadOnly();
    private readonly List<TrainingWeek> _weeks = new();

    public IReadOnlyCollection<PlanAssignment> Assignments => _assignments.AsReadOnly();
    private readonly List<PlanAssignment> _assignments = new();

    private TrainingPlan(string name, string? objective)
    {
        Name = name;
        Objective = objective;
    }

    public static TrainingPlan Create(string name, string? objective = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del plan no puede estar vacío.");

        return new TrainingPlan(name, objective);
    }
}
