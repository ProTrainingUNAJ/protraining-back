using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class TrainingPlan : BaseEntity
{
    public string Name { get; private set; }
    public string Objective { get; private set; }
    public Guid AthleteId { get; private set; }

    private readonly List<TrainingWeek> _weeks = [];
    public IReadOnlyCollection<TrainingWeek> Weeks => _weeks.AsReadOnly();

    private TrainingPlan(string name, string objective, Guid athleteId)
    {
        Name = name;
        Objective = objective;
        AthleteId = athleteId;
    }

    public static TrainingPlan Create(string name, string objective, Guid athleteId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre de la planificación no puede estar vacío.");

        return new TrainingPlan(name, objective, athleteId);
    }
}