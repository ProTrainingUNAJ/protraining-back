using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public class PlanAssignment : BaseEntity
{
    public Guid TrainingPlanId { get; private set; }
    public TrainingPlan Plan { get; private set; } = null!;

    public Guid? CategoryId { get; private set; }
    public Guid? AthleteId { get; private set; }

    public DateOnly StartDate { get; private set; }

    private PlanAssignment(Guid trainingPlanId, Guid? categoryId, Guid? athleteId, DateOnly startDate)
    {
        TrainingPlanId = trainingPlanId;
        CategoryId = categoryId;
        AthleteId = athleteId;
        StartDate = startDate;
    }

    public static PlanAssignment Create(Guid trainingPlanId, DateOnly startDate, Guid? categoryId = null, Guid? athleteId = null)
    {
        if (categoryId.HasValue && athleteId.HasValue)
            throw new DomainException("Una asignación no puede tener categoría y atleta al mismo tiempo.");

        if (!categoryId.HasValue && !athleteId.HasValue)
            throw new DomainException("La asignación debe tener al menos una categoría o un atleta.");

        return new PlanAssignment(trainingPlanId, categoryId, athleteId, startDate);
    }
}
