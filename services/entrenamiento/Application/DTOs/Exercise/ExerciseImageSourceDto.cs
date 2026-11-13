namespace Application.DTOs.Exercise;

public class ExerciseImageSourceDto
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;

    public static readonly ExerciseImageSourceDto CatalogDefault = new()
    {
        Name = "free-exercise-db",
        Url = "https://github.com/yuhonas/free-exercise-db",
        License = "The Unlicense"
    };
}

