namespace Application.DTOs.Routines
{
    public class TodayRoutineBlockDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int Order { get; set; }
        public List<TodayRoutineExerciseDto> Exercises { get; set; } = [];
    }
}
