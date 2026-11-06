namespace Application.UseCases.Exercise.UpdateVideoUrl;

public record UpdateExerciseVideoCommand(Guid ExerciseId, string? VideoUrl);