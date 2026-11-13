namespace Application.UseCases.Exercise.GetSubstitutes;

public class GetExerciseSubstitutesQuery
{
    public Guid ExerciseId { get; set; }
    public string Zone { get; set; } = string.Empty;
}

