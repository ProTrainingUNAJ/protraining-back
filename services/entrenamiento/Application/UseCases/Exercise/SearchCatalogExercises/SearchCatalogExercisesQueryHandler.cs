using Application.DTOs.Exercise;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Exercise.SearchCatalogExercises;

// E51: búsqueda de solo lectura sobre el catálogo compartido de ejercicios.
// No usa el PF del token: el catálogo es un activo común y las herramientas de IA
// no deben ver ejercicios privados de un preparador físico.
public sealed class SearchCatalogExercisesQueryHandler(IExerciseRepository exerciseRepository)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public async Task<ExerciseSearchResultDto> Handle(
        SearchCatalogExercisesQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = Clean(request.Name);
        var muscle = Clean(request.Muscle);
        var equipment = Clean(request.Equipment);

        if (name is null && muscle is null && equipment is null)
        {
            throw new ArgumentException(
                "Indicá al menos un filtro: nombre, músculo o equipamiento.",
                nameof(request));
        }

        var limit = request.Limit <= 0 ? DefaultLimit : Math.Min(request.Limit, MaxLimit);

        var (items, total) = await exerciseRepository.SearchCatalogAsync(
            name,
            equipment,
            muscle,
            page: 1,
            pageSize: limit,
            cancellationToken);

        return new ExerciseSearchResultDto(
            total,
            items.Select(ToDto).ToList());
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ExerciseCatalogItemDto ToDto(Domain.Entities.Exercise exercise) => new()
    {
        Id = exercise.Id,
        Name = exercise.Name,
        MuscleGroup = exercise.MuscleGroup,
        Equipment = exercise.Equipment,
        PrimaryMuscles = [.. exercise.PrimaryMuscles],
        SecondaryMuscles = [.. exercise.SecondaryMuscles],
        ExerciseType = exercise.ExerciseType,
        ParentExerciseId = exercise.ParentExerciseId,
        VariantLabel = exercise.VariantLabel
    };
}
