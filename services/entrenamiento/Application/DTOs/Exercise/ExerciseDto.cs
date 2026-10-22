
namespace Application.DTOs.Exercise
{
    public class ExerciseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string MuscleGroup { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? AnimationUrl { get; set; }
        public string? VideoUrl { get; set; }
        public string? Equipment { get; set; }
        public List<string> Images { get; set; } = [];
        public List<string> PrimaryMuscles { get; set; } = [];
        public List<string> SecondaryMuscles { get; set; } = [];

    }
}
