using System.ComponentModel;
using Application.DTOs.Exercise;
using Application.DTOs.Mcp;
using Application.UseCases.Exercise.GetSubstitutesByZone;
using Application.UseCases.Exercise.SearchCatalogExercises;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace API.Mcp;

// E51: herramientas MCP de solo lectura sobre la biblioteca compartida de ejercicios.
[McpServerToolType]
public sealed class ExerciseLibraryTools(
    SearchCatalogExercisesQueryHandler searchCatalogExercisesQueryHandler,
    GetSubstitutesByZoneQueryHandler getSubstitutesByZoneQueryHandler,
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

    [McpServerTool(
        Name = "ObtenerSustitutos",
        Title = "Obtener sustitutos por zona",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Obtiene ejercicios sustitutos seguros para una zona de dolor/molestia (por ejemplo, rodilla). " +
        "Devuelve la zona consultada y la lista de sustitutos disponibles.")]
    public async Task<ZoneSubstitutesResultDto> GetSubstitutes(
        [Description("Zona de dolor o molestia. Ejemplo: «rodilla».")]
        string zona,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(zona))
        {
            throw new McpException("Indicá una zona válida. Ejemplo: «rodilla».");
        }

        try
        {
            var result = await getSubstitutesByZoneQueryHandler.Handle(
                new GetSubstitutesByZoneQuery(zona),
                cancellationToken);

            // Solo métricas: no se registran datos de personas.
            logger.LogInformation(
                "ObtenerSustitutos devolvió {Count} sustitutos.",
                result.Substitutes.Count);

            return result;
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            throw new McpException(ex.Message);
        }
    }
}
