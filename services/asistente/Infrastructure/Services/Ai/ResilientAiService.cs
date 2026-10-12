using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Infrastructure.Services.Ai.Gemini;
using Infrastructure.Services.Ai.Ollama;
using Infrastructure.Services.Ai.Rules;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Ai;

public sealed class ResilientAiService(
    OllamaClient ollamaClient,
    GeminiClient geminiClient,
    RuleBasedAiClient ruleBasedClient,
    ILogger<ResilientAiService> logger) : IAiService
{
    public async Task<RoutineSuggestionResponseDto> SuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        // Tier 1: Local AI model (Ollama)
        logger.LogInformation("Intentando generar propuesta de rutina con el modelo local en Ollama.");
        var response = await ollamaClient.TrySuggestRoutineAsync(request, cancellationToken);
        if (response != null)
        {
            logger.LogInformation("Propuesta generada exitosamente por Ollama.");
            return response;
        }

        // Tier 2: Cloud AI model as fallback (Google Gemini)
        logger.LogWarning("Ollama no estuvo disponible. Activando respaldo con Google Gemini.");
        response = await geminiClient.TrySuggestRoutineAsync(request, cancellationToken);
        if (response != null)
        {
            logger.LogInformation("Propuesta generada exitosamente por el respaldo de Google Gemini.");
            return response;
        }

        // Tier 3: Deterministic local rules fallback
        logger.LogWarning("Gemini no estuvo disponible o agotó su cuota. Activando respaldo por reglas locales.");
        var ruleResponse = await ruleBasedClient.TrySuggestRoutineAsync(request, cancellationToken);
        return ruleResponse!;
    }
}
