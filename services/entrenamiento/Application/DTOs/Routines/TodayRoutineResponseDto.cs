
namespace Application.DTOs.Routines
{
    public class TodayRoutineResponseDto
    {
        public Guid AthleteId { get; set; }
        public string AthleteName { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public bool HasActiveInjuries { get; set; }
        public bool RoutineAdapted { get; set; }
        public List<TodayRoutineExerciseDto> Exercises { get; set; } = [];
    }
}
