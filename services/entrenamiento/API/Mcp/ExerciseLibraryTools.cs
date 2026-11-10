using System.ComponentModel;
using Application.DTOs.Exercise;
using Application.UseCases.Exercise.SearchCatalogExercises;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace API.Mcp;

// E51: herramienta MCP de solo lectura sobre la biblioteca compartida de ejercicios.
// El nombre público debe ser exactamente "BuscarEjercicios" (criterio de aceptación de la tarjeta).
[McpServerToolType]
public sealed class ExerciseLibraryTools(
    SearchCatalogExercisesQueryHandler searchCatalogExercisesQueryHandler,
    ILogger<ExerciseLibraryTools> logger)
{
    [McpServerTool(
        Name = "BuscarEjercicios",
        Title = "Buscar ejercicios",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Busca ejercicios de la biblioteca de ProTraining por nombre, músculo o equipamiento. " +
        "Devuelve hasta limit resultados con id, nombre, grupo muscular, equipamiento y músculos.")]
    public async Task<ExerciseSearchResultDto> SearchExercises(
        [Description("Texto del nombre del ejercicio. Ejemplo: «sentadilla».")]
        string? name = null,
        [Description("Músculo específico. Ejemplos: «cuádriceps», «glúteos», «dorsales».")]
        string? muscle = null,
        [Description("Equipamiento utilizado. Ejemplo: «barra con pesas».")]
        string? equipment = null,
        [Description("Cantidad máxima de resultados a devolver. Entre 1 y 100. Por defecto 20.")]
        int limit = SearchCatalogExercisesQueryHandler.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)
            && string.IsNullOrWhiteSpace(muscle)
            && string.IsNullOrWhiteSpace(equipment))
        {
            throw new McpException(
                "Indicá al menos un filtro: name (nombre), muscle (músculo) o equipment (equipamiento).");
        }

        var result = await searchCatalogExercisesQueryHandler.Handle(
            new SearchCatalogExercisesQuery
            {
                Name = name,
                Muscle = muscle,
                Equipment = equipment,
                Limit = limit
            },
            cancellationToken);

        // Solo métricas: no se registran los términos de búsqueda ni datos de personas.
        logger.LogInformation(
            "BuscarEjercicios devolvió {Returned} de {Total} ejercicios.",
            result.Items.Count,
            result.Total);

        return result;
    }
}
