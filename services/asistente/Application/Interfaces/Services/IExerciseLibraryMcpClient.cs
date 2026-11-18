using Application.DTOs.Mcp;

namespace Application.Interfaces.Services;

/// <summary>
/// Cliente MCP hacia el servidor de la biblioteca de ejercicios de Entrenamiento (#189 · KAN-484).
/// Permite consultar ejercicios, sustitutos y restricciones por zona a través del protocolo MCP,
/// así como listar y ejecutar herramientas dinámicamente desde el agente de IA (KAN-600).
/// </summary>
public interface IExerciseLibraryMcpClient
{
    /// <summary>
    /// Invoca la herramienta MCP <c>BuscarEjercicios</c> filtrando por nombre, músculo o equipamiento.
    /// </summary>
    Task<McpExerciseSearchResultDto> SearchExercisesAsync(
        string? name = null,
        string? muscle = null,
        string? equipment = null,
        int limit = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invoca la herramienta MCP <c>ObtenerSustitutos</c> para obtener los ejercicios sustitutos seguros de una zona de dolor.
    /// </summary>
    Task<McpZoneSubstitutesResultDto> GetSubstitutesAsync(
        string zone,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invoca la herramienta MCP <c>ObtenerRestricciones</c> para obtener los ejercicios contraindicados y sus sustitutos para una zona de dolor.
    /// </summary>
    Task<McpZoneRestrictionsResultDto> GetRestrictionsAsync(
        string zone,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene la lista de herramientas registradas en el servidor MCP de Entrenamiento con sus esquemas JSON.
    /// </summary>
    Task<IReadOnlyList<McpToolDefinitionDto>> ListToolsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta una herramienta del servidor MCP por nombre y argumentos, devolviendo el contenido estructurado en JSON.
    /// </summary>
    Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default);
}
