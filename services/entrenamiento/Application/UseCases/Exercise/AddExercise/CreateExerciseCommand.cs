using Domain.Entities;

namespace Application.UseCases.Exercise.AddExercise
{
    public class CreateExerciseCommand
    {
        public string Name { get; set; } = string.Empty;
        public string MuscleGroup { get; set; } = string.Empty;
        public List<string>? PrimaryMuscles { get; set; }
        public List<string>? SecondaryMuscles { get; set; }
        public string? Equipment { get; set; }
        public string? Instructions { get; set; }
        public Origin? Origin { get; set; }
    }
}
