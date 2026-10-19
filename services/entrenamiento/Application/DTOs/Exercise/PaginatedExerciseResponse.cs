namespace Application.DTOs.Exercise;

public record PaginatedExerciseResponse(
    List<ExerciseDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages
);
