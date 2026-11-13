namespace Application.DTOs.Exercise;

public class CheckExerciseRestrictionsRequest
{
    public string? PainZones { get; set; }
    public List<Guid>? ExerciseIds { get; set; }
}

