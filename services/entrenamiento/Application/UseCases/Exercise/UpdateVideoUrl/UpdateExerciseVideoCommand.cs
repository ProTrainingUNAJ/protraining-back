namespace Application.UseCases.Exercise.UpdateVideoUrl
{
    public class UpdateExerciseVideoCommand
    {
        public Guid ExerciseId { get; set; }
        public string? VideoUrl { get; set; }
    }
}
