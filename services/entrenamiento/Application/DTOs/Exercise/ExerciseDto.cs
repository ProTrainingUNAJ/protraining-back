
using Domain.Entities;

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
        public Origin Origin { get; set; }

        // Variantes (KAN-554). En una variante, el id de su ejercicio base y la etiqueta
        // ("Agarre cerrado"). Variants solo viene con groupVariants=true, en los ejercicios base.
        public Guid? ParentExerciseId { get; set; }
        public string? VariantLabel { get; set; }
        public List<ExerciseDto>? Variants { get; set; }

    }
}
