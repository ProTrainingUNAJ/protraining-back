namespace Infrastructure.Services.Ai.Agent;

/// <summary>
/// Opciones de configuración para el agente de IA genérico y su cadena de proveedores (OpenAI / Gemini).
/// Permite definir tiempos límites, límites de llamadas a herramientas MCP y la lista ordenada de proveedores.
/// </summary>
public sealed class AgentOptions
{
    public const string SectionName = "Ai:Agent";

    /// <summary>
    /// Cantidad máxima permitida de ejecuciones de herramientas MCP por pedido (obligatorio: 6).
    /// </summary>
    public int MaxToolCalls { get; set; } = 6;

    /// <summary>
    /// Tiempo límite total por defecto en segundos para la interacción con el proveedor (default: 40s).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 40;

    /// <summary>
    /// Lista ordenada de proveedores compatibles con la API de OpenAI (ej. OpenAI -> Gemini).
    /// </summary>
    public List<AgentProviderOptions> Providers { get; set; } = [];
}

/// <summary>
/// Configuración específica de un proveedor compatible con la API de OpenAI (OpenAI, Azure OpenAI, Gemini).
/// </summary>
public sealed class AgentProviderOptions
{
    /// <summary>
    /// Nombre identificador del proveedor (ej. "OpenAI", "Gemini").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// URL base del endpoint compatible con Chat Completions de OpenAI.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Identificador del modelo (ej. "gpt-4.1-mini", "gemini-2.5-flash").
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Clave de API para autenticación (Bearer o api-key).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Tiempo límite específico en segundos para este proveedor.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 40;
}
