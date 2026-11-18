using System.Text.Json;
using Application.DTOs.Mcp;
using Application.Exceptions;
using Application.Interfaces.Services;
using Domain.Exceptions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Infrastructure.Services.Mcp;

/// <summary>
/// Cliente MCP hacia el servidor de la biblioteca de ejercicios de Entrenamiento (#189 · KAN-484).
/// Se conecta mediante Streamable HTTP al endpoint <c>/mcp</c> de Entrenamiento utilizando el
/// <see cref="HttpClient"/> inyectado (con <c>ForwardAuthorizationHandler</c> y resiliencia Polly).
/// </summary>
public sealed class ExerciseLibraryMcpClient : IExerciseLibraryMcpClient
{
    public const string McpRelativePath = "mcp";
    public const string BuscarEjerciciosToolName = "BuscarEjercicios";
    public const string ObtenerSustitutosToolName = "ObtenerSustitutos";
    public const string ObtenerRestriccionesToolName = "ObtenerRestricciones";

    public const int DefaultSearchLimit = 20;
    public const int MaxSearchLimit = 100;

    public const string McpUnavailableMessage =
        "No se pudo consultar la biblioteca de ejercicios por MCP en Entrenamiento. Probá de nuevo en unos minutos.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<ExerciseLibraryMcpClient> _logger;

    public ExerciseLibraryMcpClient(
        HttpClient httpClient,
        ILogger<ExerciseLibraryMcpClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<McpExerciseSearchResultDto> SearchExercisesAsync(
        string? name = null,
        string? muscle = null,
        string? equipment = null,
        int limit = DefaultSearchLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)
            && string.IsNullOrWhiteSpace(muscle)
            && string.IsNullOrWhiteSpace(equipment))
        {
            throw new DomainException(
                "Indicá al menos un filtro para buscar ejercicios por MCP: nombre, músculo o equipamiento.");
        }

        var normalizedLimit = limit <= 0 ? DefaultSearchLimit : Math.Min(limit, MaxSearchLimit);
        var arguments = new Dictionary<string, object?>
        {
            ["limit"] = normalizedLimit
        };

        if (!string.IsNullOrWhiteSpace(name))
        {
            arguments["name"] = name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(muscle))
        {
            arguments["muscle"] = muscle.Trim();
        }

        if (!string.IsNullOrWhiteSpace(equipment))
        {
            arguments["equipment"] = equipment.Trim();
        }

        var json = await CallToolAsync(BuscarEjerciciosToolName, arguments, cancellationToken);
        var result = DeserializeResult<McpExerciseSearchResultDto>(json, BuscarEjerciciosToolName);

        _logger.LogInformation(
            "Cliente MCP {ToolName} obtuvo {Returned} de {Total} ejercicios.",
            BuscarEjerciciosToolName,
            result.Items.Count,
            result.Total);

        return result;
    }

    public async Task<McpZoneSubstitutesResultDto> GetSubstitutesAsync(
        string zone,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(zone))
        {
            throw new DomainException("Indicá una zona válida para buscar sustitutos por MCP. Ejemplo: «rodilla».");
        }

        var arguments = new Dictionary<string, object?>
        {
            ["zona"] = zone.Trim()
        };

        var json = await CallToolAsync(ObtenerSustitutosToolName, arguments, cancellationToken);
        var result = DeserializeResult<McpZoneSubstitutesResultDto>(json, ObtenerSustitutosToolName);

        _logger.LogInformation(
            "Cliente MCP {ToolName} obtuvo {Count} sustitutos para la zona {Zone}.",
            ObtenerSustitutosToolName,
            result.Substitutes.Count,
            result.Zone);

        return result;
    }

    public async Task<McpZoneRestrictionsResultDto> GetRestrictionsAsync(
        string zone,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(zone))
        {
            throw new DomainException("Indicá una zona válida para consultar restricciones por MCP. Ejemplo: «rodilla».");
        }

        var arguments = new Dictionary<string, object?>
        {
            ["zona"] = zone.Trim()
        };

        var json = await CallToolAsync(ObtenerRestriccionesToolName, arguments, cancellationToken);
        var result = DeserializeResult<McpZoneRestrictionsResultDto>(json, ObtenerRestriccionesToolName);

        _logger.LogInformation(
            "Cliente MCP {ToolName} obtuvo {Count} restricciones para la zona {Zone}.",
            ObtenerRestriccionesToolName,
            result.Restrictions.Count,
            result.Zone);

        return result;
    }

    public async Task<IReadOnlyList<McpToolDefinitionDto>> ListToolsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var mcpClient = await CreateConnectedClientAsync(cancellationToken);
            var tools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);

            return tools
                .Select(t => new McpToolDefinitionDto(
                    t.Name,
                    t.Description,
                    t.JsonSchema))
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TrainingServiceUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "No se pudo listar las herramientas del servidor MCP de Entrenamiento: {ErrorType}.",
                ex.GetType().Name);
            throw new TrainingServiceUnavailableException(McpUnavailableMessage, ex);
        }
    }

    public async Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new DomainException("El nombre de la herramienta MCP es obligatorio.");
        }

        CallToolResult callResult;
        try
        {
            await using var mcpClient = await CreateConnectedClientAsync(cancellationToken);
            callResult = await mcpClient.CallToolAsync(
                toolName.Trim(),
                arguments,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DomainException)
        {
            throw;
        }
        catch (TrainingServiceUnavailableException)
        {
            throw;
        }
        catch (McpException ex)
        {
            _logger.LogWarning(
                ex,
                "El servidor MCP de Entrenamiento rechazó la invocación de la herramienta {ToolName}.",
                toolName);
            throw new DomainException(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Error de comunicación con el servidor MCP de Entrenamiento al invocar {ToolName}: {ErrorType}.",
                toolName,
                ex.GetType().Name);
            throw new TrainingServiceUnavailableException(McpUnavailableMessage, ex);
        }

        if (callResult.IsError == true)
        {
            var errorMessage = ExtractErrorText(callResult, toolName);
            _logger.LogWarning(
                "La herramienta MCP {ToolName} devolvió IsError=true: {ErrorMessage}",
                toolName,
                errorMessage);
            throw new DomainException(errorMessage);
        }

        return ExtractJsonPayload(callResult, toolName);
    }

    private async Task<McpClient> CreateConnectedClientAsync(CancellationToken cancellationToken)
    {
        var endpoint = ResolveMcpEndpoint(_httpClient.BaseAddress);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                Name = "asistente-entrenamiento-mcp"
            },
            _httpClient,
            ownsHttpClient: false);

        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    public static Uri ResolveMcpEndpoint(Uri? baseAddress)
    {
        if (baseAddress is null)
        {
            throw new TrainingServiceUnavailableException(McpUnavailableMessage);
        }

        var raw = baseAddress.ToString().TrimEnd('/');
        if (raw.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(raw, UriKind.Absolute);
        }

        return new Uri($"{raw}/{McpRelativePath}", UriKind.Absolute);
    }

    private static string ExtractJsonPayload(CallToolResult callResult, string toolName)
    {
        if (callResult.StructuredContent is { } structured)
        {
            return JsonSerializer.Serialize(structured, JsonOptions);
        }

        var textBlock = callResult.Content?
            .OfType<TextContentBlock>()
            .FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.Text));

        if (textBlock is not null)
        {
            return textBlock.Text;
        }

        if (callResult.Content is { Count: > 0 })
        {
            return JsonSerializer.Serialize(callResult.Content, JsonOptions);
        }

        throw new TrainingServiceUnavailableException(
            $"El servidor MCP de Entrenamiento devolvió una respuesta vacía para la herramienta '{toolName}'.");
    }

    private static string ExtractErrorText(CallToolResult callResult, string toolName)
    {
        var textBlock = callResult.Content?
            .OfType<TextContentBlock>()
            .FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.Text));

        if (!string.IsNullOrWhiteSpace(textBlock?.Text))
        {
            return textBlock.Text;
        }

        return $"Error al ejecutar la herramienta MCP '{toolName}' en Entrenamiento.";
    }

    private T DeserializeResult<T>(string json, string toolName)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<T>(json, JsonOptions);
            if (parsed is null)
            {
                throw new TrainingServiceUnavailableException(
                    $"No se pudo interpretar la respuesta de la herramienta MCP '{toolName}'.");
            }

            return parsed;
        }
        catch (TrainingServiceUnavailableException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "No se pudo deserializar la respuesta JSON de la herramienta MCP {ToolName}.",
                toolName);
            throw new TrainingServiceUnavailableException(
                $"No se pudo interpretar la respuesta de la herramienta MCP '{toolName}'.",
                ex);
        }
    }
}
