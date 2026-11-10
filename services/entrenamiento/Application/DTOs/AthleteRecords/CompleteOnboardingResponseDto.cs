namespace Application.DTOs.AthleteRecords;

public class CompleteOnboardingResponseDto
{
    public List<AthleteRecordDto> Created { get; set; } = new();
    public List<SkippedOnboardingRecordDto> Skipped { get; set; } = new();

    // Alias para compatibilidad con consumidores que esperan "Records".
    public List<AthleteRecordDto> Records => Created;
    public List<SkippedOnboardingRecordDto> SkippedExercises => Skipped;
}

public class SkippedOnboardingRecordDto
{
    public Guid ExerciseId { get; set; }
    public string ExerciseName { get; set; } = string.Empty;

    // Alias para compatibilidad.
    public Guid Id => ExerciseId;
    public string Name => ExerciseName;
}