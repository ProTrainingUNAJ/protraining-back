namespace Application.DTOs.AthleteRecords;

public class CompleteOnboardingResponseDto
{
    public List<AthleteRecordDto> Created { get; set; } = new();
    public List<SkippedOnboardingRecordDto> Skipped { get; set; } = new();
}

public class SkippedOnboardingRecordDto
{
    public Guid ExerciseId { get; set; }
    public string ExerciseName { get; set; } = string.Empty;
}