namespace Application.UseCases.Exercise.SearchCatalogExercises;

// E51: filtros de la herramienta MCP BuscarEjercicios. Al menos uno debe venir informado.
public sealed class SearchCatalogExercisesQuery
{
    public string? Name { get; set; }
    public string? Muscle { get; set; }
    public string? Equipment { get; set; }
    public int Limit { get; set; } = SearchCatalogExercisesQueryHandler.DefaultLimit;
}
