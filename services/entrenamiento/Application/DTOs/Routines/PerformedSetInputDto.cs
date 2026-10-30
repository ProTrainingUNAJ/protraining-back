namespace Application.DTOs.Routines;

public class PerformedSetInputDto
{
    public Guid ExerciseId { get; set; }
    public decimal Weight { get; set; }
    public int Reps { get; set; }
}

