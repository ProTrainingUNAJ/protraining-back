namespace Application.UseCases.Exercise.ListExercises
{
    public class ListExercisesQuery
    {
        public string? Search { get; set; }
        public string? MuscleGroup { get; set; }
        public string? Equipment { get; set; }
        public bool UsedByPf { get; set; }
        // KAN-554: true → solo los ejercicios base, con sus variantes adentro.
        public bool GroupVariants { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
