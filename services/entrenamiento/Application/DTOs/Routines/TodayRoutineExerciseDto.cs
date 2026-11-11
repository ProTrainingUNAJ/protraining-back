
using Domain.Entities;

namespace Application.DTOs.Routines
{
    public class TodayRoutineExerciseDto
    {
        public Guid ExerciseId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string MuscleGroup { get; set; } = string.Empty;
        public string? VideoUrl { get; set; }
        // Fotos del ejercicio (inicio y fin del movimiento), las mismas que la biblioteca
        public List<string> Images { get; set; } = [];

        // Ilustraciones o animaciones de terceros, en orden de preferencia (KAN-549).
        public List<Application.DTOs.Exercise.ExerciseMediaDto> Media { get; set; } = [];
        public List<string> PrimaryMuscles { get; set; } = [];
        public List<string> SecondaryMuscles { get; set; } = [];
        public ExerciseType ExerciseType { get; set; } = ExerciseType.Weight;
        public int Sets { get; set; }
        public int Reps { get; set; }
        public double Percentage { get; set; }
        public double? WeightKg { get; set; }
        public double? BarWeight { get; set; }
        public double? WeightPerSide { get; set; } 
        public double? Current1RmKg { get; set; }
        public bool IsReference { get; set; }
        public double? TargetTimeSeconds { get; set; }
        public double? BestMarkSeconds { get; set; }
    }
}
