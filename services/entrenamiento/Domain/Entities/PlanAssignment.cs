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

    // Sesión que toca en una fecha: la semana sale de los días desde el inicio (semana 1 = los
    // primeros 7 días) y la sesión, del día de la semana. Necesita el plan con semanas y sesiones.
    public TrainingSession? SessionOn(DateOnly date)
    {
        if (date < StartDate)
            return null;

        var weekNumber = WeekNumberOn(date);

        return Plan.Weeks
            .FirstOrDefault(week => week.WeekNumber == weekNumber)?
            .Sessions.FirstOrDefault(session => session.DayOfWeek == date.DayOfWeek);
    }

    public int WeekNumberOn(DateOnly date) => (date.DayNumber - StartDate.DayNumber) / 7 + 1;
}
