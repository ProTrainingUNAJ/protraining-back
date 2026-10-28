namespace Application.UseCases.Exercise.CheckRestrictions;

public class CheckExerciseRestrictionsQuery
{
    public string? PainZones { get; set; }
    public List<Guid> ExerciseIds { get; set; } = [];
}

