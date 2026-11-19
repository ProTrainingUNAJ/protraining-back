using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Application.Common.Ai;
using Application.Common.DayAdjustments;
using Application.Common.PlanDrafts;
using Application.Common.Recommendations;
using Application.DTOs.Ai;
using Application.DTOs.DayAdjustments;
using Application.DTOs.Mcp;
using Application.DTOs.PlanDrafts;
using Application.DTOs.Recommendations;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Services.Mcp;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Ai.Agent;

/// <summary>
/// Cliente de agente de IA genérico compatible con el protocolo OpenAI Chat Completions (OpenAI, Azure OpenAI, Gemini).
/// Orquesta el ciclo de toma de decisiones del agente invocando herramientas MCP de entrenamiento
/// antes de consolidar el borrador en JSON estructurado.
/// </summary>
public sealed class GenericAgentAiClient : IAiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AgentProviderOptions _providerOptions;
    private readonly HttpClient _httpClient;
    private readonly IExerciseLibraryMcpClient _mcpClient;
    private readonly IAiAuditService _aiAuditService;
    private readonly ILogger<GenericAgentAiClient> _logger;
    private readonly IExerciseCatalogService? _catalogService;
    private readonly int _maxToolCalls;

    public GenericAgentAiClient(
        AgentProviderOptions providerOptions,
        HttpClient httpClient,
        IExerciseLibraryMcpClient mcpClient,
        IAiAuditService aiAuditService,
        ILogger<GenericAgentAiClient> logger,
        IExerciseCatalogService? catalogService = null,
        int maxToolCalls = 6)
    {
        _providerOptions = providerOptions ?? throw new ArgumentNullException(nameof(providerOptions));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _mcpClient = mcpClient ?? throw new ArgumentNullException(nameof(mcpClient));
        _aiAuditService = aiAuditService ?? throw new ArgumentNullException(nameof(aiAuditService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _catalogService = catalogService;
        _maxToolCalls = maxToolCalls > 0 ? maxToolCalls : 6;
    }

    public string ProviderName =>
        string.IsNullOrWhiteSpace(_providerOptions.Name)
            ? "Agente (OpenAI)"
            : (_providerOptions.Name.StartsWith("Agente", StringComparison.OrdinalIgnoreCase)
                ? _providerOptions.Name
                : $"Agente ({_providerOptions.Name})");

    public async Task<PlanDraftContentDto?> TryGeneratePlanDraftAsync(
        CreatePlanDraftRequestDto request,
        IReadOnlyList<DateOnly>? matchDates = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var overallStopwatch = Stopwatch.StartNew();

        // Si el proveedor no tiene API key configurada, se saltea sin error según protocolo
        if (string.IsNullOrWhiteSpace(_providerOptions.ApiKey))
        {
            overallStopwatch.Stop();
            _logger.LogInformation(
                "El proveedor {Provider} no tiene ApiKey configurada. Omitiendo y pasando al siguiente respaldo.",
                ProviderName);
            await SafeAuditAsync(
                prompt: $"PlanDraft: {request.Goal}",
                latencyMs: overallStopwatch.ElapsedMilliseconds,
                isValid: false,
                validationResult: "SinApiKey",
                athleteId: request.AthleteId,
                errorMessage: $"El proveedor {ProviderName} no tiene ApiKey configurada.");
            return null;
        }

        // Obtener la lista de herramientas disponibles desde el servidor MCP de Entrenamiento
        var availableTools = await ResolveMcpToolsAsync(cancellationToken);

        var conversation = new List<OpenAiChatMessage>
        {
            OpenAiChatMessage.CreateSystem(AgentPromptBuilder.SystemPrompt),
            OpenAiChatMessage.CreateUser(AgentPromptBuilder.BuildPlanDraftUserPrompt(request, matchDates))
        };

        var toolCallsCount = 0;
        const int maxTurns = 8;
        var turn = 0;
        var accumulatedPromptTokens = 0;
        var accumulatedCompletionTokens = 0;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _providerOptions.TimeoutSeconds)));

        while (turn < maxTurns)
        {
            turn++;
            cancellationToken.ThrowIfCancellationRequested();

            var requestPayload = new OpenAiChatRequest
            {
                Model = _providerOptions.Model,
                Messages = conversation,
                Tools = toolCallsCount < _maxToolCalls && availableTools.Count > 0 ? availableTools : null,
                ToolChoice = toolCallsCount >= _maxToolCalls ? "none" : null,
                ResponseFormat = new OpenAiResponseFormat { Type = "json_object" },
                Temperature = 0.2
            };

            var turnStopwatch = Stopwatch.StartNew();
            OpenAiChatResponse? response;

            try
            {
                response = await SendChatCompletionAsync(requestPayload, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                turnStopwatch.Stop();
                overallStopwatch.Stop();
                _logger.LogWarning(
                    "El agente {Provider} superó el tiempo límite de {Seconds}s. Pasando al siguiente respaldo.",
                    ProviderName,
                    _providerOptions.TimeoutSeconds);
                await SafeAuditAsync(
                    prompt: $"PlanDraft: {request.Goal} (Turno {turn})",
                    latencyMs: overallStopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "Timeout",
                    athleteId: request.AthleteId,
                    errorMessage: $"Tiempo límite de {_providerOptions.TimeoutSeconds}s superado.");
                return null;
            }
            catch (Exception ex)
            {
                turnStopwatch.Stop();
                overallStopwatch.Stop();
                _logger.LogWarning(
                    ex,
                    "Error de comunicación con el agente {Provider}: {Message}. Pasando al siguiente respaldo.",
                    ProviderName,
                    ex.Message);
                await SafeAuditAsync(
                    prompt: $"PlanDraft: {request.Goal} (Turno {turn})",
                    latencyMs: overallStopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "ErrorComunicacion",
                    athleteId: request.AthleteId,
                    errorMessage: ex.Message);
                return null;
            }

            turnStopwatch.Stop();

            if (response?.Usage != null)
            {
                accumulatedPromptTokens += response.Usage.PromptTokens;
                accumulatedCompletionTokens += response.Usage.CompletionTokens;
            }

            var choice = response?.Choices.FirstOrDefault();
            if (choice == null)
            {
                _logger.LogWarning("El modelo {Provider} no devolvió opciones (choices) en su respuesta.", ProviderName);
                return null;
            }

            var assistantMessage = choice.Message;

            // Verificar si el modelo solicitó invocar herramientas MCP
            if (assistantMessage.ToolCalls is { Count: > 0 } && toolCallsCount < _maxToolCalls)
            {
                conversation.Add(assistantMessage);

                foreach (var toolCall in assistantMessage.ToolCalls)
                {
                    if (toolCallsCount >= _maxToolCalls)
                    {
                        _logger.LogInformation("Se alcanzó el límite de {Limit} llamadas a tools MCP por pedido.", _maxToolCalls);
                        break;
                    }

                    var toolStopwatch = Stopwatch.StartNew();
                    var toolResult = await ExecuteToolCallAsync(toolCall, cancellationToken);
                    toolStopwatch.Stop();
                    toolCallsCount++;

                    conversation.Add(OpenAiChatMessage.CreateTool(toolCall.Id, toolResult));

                    // Registro auditable de la ejecución de la herramienta MCP
                    await SafeAuditAsync(
                        prompt: $"ToolCall: {toolCall.Function.Name}({toolCall.Function.Arguments})",
                        latencyMs: toolStopwatch.ElapsedMilliseconds,
                        isValid: true,
                        validationResult: $"McpTool:{toolCall.Function.Name}",
                        response: toolResult,
                        athleteId: request.AthleteId);
                }

                continue;
            }

            // Si el modelo devolvió contenido final sin tool calls o ya se agotaron las herramientas
            if (!string.IsNullOrWhiteSpace(assistantMessage.Content))
            {
                overallStopwatch.Stop();
                var cleanedJson = AiRoutineParser.CleanMarkdownFences(assistantMessage.Content);

                // Obtener catálogo para traducción de índices y validación si está disponible
                IReadOnlyList<CatalogExerciseDto> catalog = [];
                if (_catalogService != null)
                {
                    try
                    {
                        catalog = await _catalogService.GetExercisesForGoalAsync(request.Goal, 30, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "No se pudo consultar el catálogo para la traducción por bloques.");
                    }
                }

                var parseResult = PlanDraftBlockParser.ParseAndExpand(
                    cleanedJson,
                    catalog,
                    request.Weeks,
                    request.StartDate,
                    matchDates);

                if (parseResult.IsValid && parseResult.Content != null)
                {
                    await SafeAuditAsync(
                        prompt: $"PlanDraft: {request.Goal}",
                        latencyMs: overallStopwatch.ElapsedMilliseconds,
                        isValid: true,
                        validationResult: "Success",
                        response: assistantMessage.Content,
                        athleteId: request.AthleteId,
                        promptTokens: accumulatedPromptTokens,
                        completionTokens: accumulatedCompletionTokens,
                        totalTokens: accumulatedPromptTokens + accumulatedCompletionTokens);

                    return parseResult.Content;
                }

                // Fallback directo si el JSON ya era PlanDraftContentDto
                var directContent = PlanDraftMapper.DeserializeContent(cleanedJson);
                if (directContent?.Weeks is { Count: > 0 })
                {
                    await SafeAuditAsync(
                        prompt: $"PlanDraft: {request.Goal}",
                        latencyMs: overallStopwatch.ElapsedMilliseconds,
                        isValid: true,
                        validationResult: "SuccessDirect",
                        response: assistantMessage.Content,
                        athleteId: request.AthleteId,
                        promptTokens: accumulatedPromptTokens,
                        completionTokens: accumulatedCompletionTokens,
                        totalTokens: accumulatedPromptTokens + accumulatedCompletionTokens);

                    return directContent;
                }

                _logger.LogWarning(
                    "El borrador generado por {Provider} no pudo ser interpretado: {Error}",
                    ProviderName,
                    parseResult.ErrorMessage ?? "JSON no compatible");

                await SafeAuditAsync(
                    prompt: $"PlanDraft: {request.Goal}",
                    latencyMs: overallStopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "JsonInvalido",
                    response: assistantMessage.Content,
                    athleteId: request.AthleteId,
                    promptTokens: accumulatedPromptTokens,
                    completionTokens: accumulatedCompletionTokens,
                    errorMessage: parseResult.ErrorMessage);

                return null;
            }
        }

        overallStopwatch.Stop();
        _logger.LogWarning(
            "El agente {Provider} agotó el ciclo de conversación sin producir un borrador válido.",
            ProviderName);
        return null;
    }

    public async Task<string?> TryGenerateDayAdjustmentJustificationAsync(
        IReadOnlyList<DayAdjustmentItemDto> items,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_providerOptions.ApiKey) || items == null || items.Count == 0)
        {
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        var prompt = AgentPromptBuilder.BuildDayAdjustmentPrompt(items);

        var requestPayload = new OpenAiChatRequest
        {
            Model = _providerOptions.Model,
            Messages =
            [
                OpenAiChatMessage.CreateSystem(AgentPromptBuilder.SystemPrompt),
                OpenAiChatMessage.CreateUser(prompt)
            ],
            Temperature = 0.3,
            MaxTokens = 250
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Min(20, _providerOptions.TimeoutSeconds)));

        try
        {
            var response = await SendChatCompletionAsync(requestPayload, timeoutCts.Token);
            stopwatch.Stop();

            var text = response?.Choices.FirstOrDefault()?.Message.Content?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                await SafeAuditAsync(
                    prompt: prompt,
                    latencyMs: stopwatch.ElapsedMilliseconds,
                    isValid: true,
                    validationResult: "SuccessDayAdjustment",
                    response: text,
                    promptTokens: response?.Usage?.PromptTokens,
                    completionTokens: response?.Usage?.CompletionTokens,
                    totalTokens: response?.Usage?.TotalTokens);
                return text;
            }

            return null;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Error al redactar justificación de ajuste con {Provider}: {Message}", ProviderName, ex.Message);
            return null;
        }
    }

    public async Task<RoutineSuggestionResponseDto?> TrySuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_providerOptions.ApiKey))
        {
            return null;
        }

        var draftRequest = new CreatePlanDraftRequestDto(
            AthleteId: request.AthleteId,
            Goal: request.Goal,
            Weeks: 1,
            DaysPerWeek: 1,
            StartDate: DateOnly.FromDateTime(DateTime.UtcNow));

        var draftContent = await TryGeneratePlanDraftAsync(draftRequest, null, cancellationToken);
        if (draftContent?.Weeks is { Count: > 0 } && draftContent.Weeks[0].Sessions is { Count: > 0 })
        {
            var session = draftContent.Weeks[0].Sessions[0];
            var exercises = session.Exercises.Select(e => new SuggestedExerciseDto(
                ExerciseName: e.ExerciseName ?? "Ejercicio",
                Sets: e.Sets,
                Reps: e.Reps,
                SuggestedPercentage: e.TargetPercentage ?? 70m,
                ExerciseId: e.ExerciseId,
                IsReference: e.IsReference
            )).ToList();

            return new RoutineSuggestionResponseDto(
                AthleteId: request.AthleteId,
                ProposedTitle: session.Name,
                JustificationSummary: draftContent.JustificationSummary,
                Exercises: exercises,
                ProviderUsed: ProviderName
            );
        }

        return null;
    }

    public Task<TrainingRecommendationResponseDto?> TryGenerateTrainingRecommendationAsync(
        TrainingRecommendationRequestDto request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<TrainingRecommendationResponseDto?>(null);

    public Task<NutritionRecommendationResponseDto?> TryGenerateNutritionRecommendationAsync(
        NutritionRecommendationRequestDto request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<NutritionRecommendationResponseDto?>(null);

    private async Task<OpenAiChatResponse?> SendChatCompletionAsync(
        OpenAiChatRequest payload,
        CancellationToken cancellationToken)
    {
        var targetUri = ResolveChatCompletionsUri(_providerOptions.BaseUrl);
        var jsonContent = JsonSerializer.Serialize(payload, JsonOptions);

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, targetUri)
        {
            Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_providerOptions.ApiKey))
        {
            requestMessage.Headers.TryAddWithoutValidation("api-key", _providerOptions.ApiKey);
            requestMessage.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_providerOptions.ApiKey}");
        }

        using var response = await _httpClient.SendAsync(requestMessage, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "El proveedor {Provider} respondió con estado HTTP {StatusCode}: {ErrorBody}",
                ProviderName,
                (int)response.StatusCode,
                errorBody);
            return null;
        }

        var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<OpenAiChatResponse>(responseString, JsonOptions);
    }

    private async Task<string> ExecuteToolCallAsync(
        OpenAiToolCall toolCall,
        CancellationToken cancellationToken)
    {
        var toolName = toolCall.Function.Name;
        IReadOnlyDictionary<string, object?>? arguments = null;

        if (!string.IsNullOrWhiteSpace(toolCall.Function.Arguments))
        {
            try
            {
                arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                    toolCall.Function.Arguments,
                    JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al deserializar los argumentos de la tool {ToolName}.", toolName);
            }
        }

        // Normalización amigable de argumentos comunes (ej. zone vs zona)
        if (arguments != null)
        {
            var normalized = new Dictionary<string, object?>(arguments, StringComparer.OrdinalIgnoreCase);
            if (normalized.TryGetValue("zone", out var zoneVal) && !normalized.ContainsKey("zona"))
            {
                normalized["zona"] = zoneVal;
            }
            arguments = normalized;
        }

        try
        {
            return await _mcpClient.CallToolAsync(toolName, arguments, cancellationToken);
        }
        catch (DomainException dex)
        {
            _logger.LogWarning("La herramienta MCP {ToolName} devolvió una excepción de dominio: {Message}", toolName, dex.Message);
            return JsonSerializer.Serialize(new { error = dex.Message }, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo al ejecutar la herramienta MCP {ToolName}: {Message}", toolName, ex.Message);
            return JsonSerializer.Serialize(new { error = $"Error de comunicación al invocar la herramienta {toolName}." }, JsonOptions);
        }
    }

    private async Task<List<OpenAiToolDefinition>> ResolveMcpToolsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var mcpDefinitions = await _mcpClient.ListToolsAsync(cancellationToken);
            if (mcpDefinitions != null && mcpDefinitions.Count > 0)
            {
                return mcpDefinitions.Select(d => new OpenAiToolDefinition
                {
                    Type = "function",
                    Function = new OpenAiFunctionDefinition
                    {
                        Name = d.Name,
                        Description = d.Description,
                        Parameters = d.InputSchema
                    }
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "No se pudieron consultar dinámicamente las herramientas MCP. Se cargarán las definiciones estandarizadas.");
        }

        return GetStandardFallbackTools();
    }

    private static List<OpenAiToolDefinition> GetStandardFallbackTools()
    {
        using var searchDoc = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "name": { "type": "string", "description": "Nombre del ejercicio a buscar" },
                "muscle": { "type": "string", "description": "Músculo o grupo muscular" },
                "equipment": { "type": "string", "description": "Equipamiento requerido" },
                "limit": { "type": "integer", "description": "Cantidad máxima de resultados (1-100)" }
              }
            }
            """);

        using var subsDoc = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "zona": { "type": "string", "description": "Zona de dolor o molestia (ej. 'rodilla', 'hombro')" }
              },
              "required": ["zona"]
            }
            """);

        using var restrDoc = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "zona": { "type": "string", "description": "Zona de dolor o molestia (ej. 'rodilla', 'hombro')" }
              },
              "required": ["zona"]
            }
            """);

        return
        [
            new OpenAiToolDefinition
            {
                Type = "function",
                Function = new OpenAiFunctionDefinition
                {
                    Name = ExerciseLibraryMcpClient.BuscarEjerciciosToolName,
                    Description = "Busca ejercicios en la biblioteca compartida de ProTraining por nombre, músculo o equipamiento.",
                    Parameters = searchDoc.RootElement.Clone()
                }
            },
            new OpenAiToolDefinition
            {
                Type = "function",
                Function = new OpenAiFunctionDefinition
                {
                    Name = ExerciseLibraryMcpClient.ObtenerSustitutosToolName,
                    Description = "Obtiene ejercicios sustitutos seguros para una zona de dolor o molestia biomecánica.",
                    Parameters = subsDoc.RootElement.Clone()
                }
            },
            new OpenAiToolDefinition
            {
                Type = "function",
                Function = new OpenAiFunctionDefinition
                {
                    Name = ExerciseLibraryMcpClient.ObtenerRestriccionesToolName,
                    Description = "Obtiene los ejercicios contraindicados y sus sustitutos autorizados ante dolor o molestia.",
                    Parameters = restrDoc.RootElement.Clone()
                }
            }
        ];
    }

    public static Uri ResolveChatCompletionsUri(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("BaseUrl no puede estar vacío.", nameof(baseUrl));
        }

        var trimmed = baseUrl.Trim();
        if (trimmed.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(trimmed, UriKind.Absolute);
        }

        var queryIndex = trimmed.IndexOf('?');
        string pathPart;
        var queryString = string.Empty;

        if (queryIndex >= 0)
        {
            pathPart = trimmed[..queryIndex].TrimEnd('/');
            queryString = trimmed[queryIndex..];
        }
        else
        {
            pathPart = trimmed.TrimEnd('/');
        }

        return new Uri($"{pathPart}/chat/completions{queryString}", UriKind.Absolute);
    }

    private async Task SafeAuditAsync(
        string prompt,
        long latencyMs,
        bool isValid,
        string validationResult,
        string? response = null,
        Guid? athleteId = null,
        int? promptTokens = null,
        int? completionTokens = null,
        int? totalTokens = null,
        string? errorMessage = null)
    {
        try
        {
            var log = AiLog.Create(
                provider: ProviderName,
                prompt: prompt,
                latencyMs: latencyMs,
                isValid: isValid,
                validationResult: validationResult,
                response: response,
                athleteId: athleteId,
                promptTokens: promptTokens,
                completionTokens: completionTokens,
                totalTokens: totalTokens,
                errorMessage: errorMessage);

            await _aiAuditService.LogAsync(log);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al registrar telemetría de IA en ai_logs para {Provider}.", ProviderName);
        }
    }
}
