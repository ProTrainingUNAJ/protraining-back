
namespace Application.UseCases.Routines.GetTodayRoutine
{
    public class GetTodayRoutineQuery
    {
        public Guid? AthleteId { get; set; }

        // Fecha de la rutina (hora Argentina). Sin fecha, la de hoy.
        public DateOnly? Date { get; set; }
    }
}
