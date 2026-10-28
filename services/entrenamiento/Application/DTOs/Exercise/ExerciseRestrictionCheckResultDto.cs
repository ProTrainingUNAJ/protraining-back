namespace Application.DTOs.Exercise;

public class ExerciseRestrictionCheckResultDto
{
    public Guid ExerciseId { get; set; }
    public bool Restricted { get; set; }
    public string? Zone { get; set; }
    public int? PainLevel { get; set; }
    public string? Reason { get; set; }
    public List<ExerciseSubstituteDto> Substitutes { get; set; } = [];
}

