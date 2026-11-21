namespace Application.DTOs.Routines
{
    public class TodayRoutineResponseDto
    {
        public Guid AthleteId { get; set; }
        public string AthleteName { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public Guid? SessionId { get; set; }
        public bool HasActiveInjuries { get; set; }
        public bool RoutineAdapted { get; set; }
        public bool AdjustedByCoach { get; set; }
        public string? AdjustmentNote { get; set; }
        public string? CoachNotes { get; set; }
        // Hora de la sesión (hora Argentina), si el PF la cargó
        public TimeOnly? StartTime { get; set; }
        public List<TodayRoutineExerciseDto> Exercises { get; set; } = [];
        // Valores semanales / bloques resueltos
        public int? WeekNumber { get; set; }
        public List<TodayRoutineBlockDto> Blocks { get; set; } = [];
        public string? SessionName { get; set; }
    }
}
