using System.Diagnostics;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Infrastructure.Services.Ai.Gemini;
using Infrastructure.Services.Ai.Ollama;
using Infrastructure.Services.Ai.Rules;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Ai;

/// <summary>
/// Orchestrates the resilient AI provider chain:
/// 1. Ollama (local model)
/// 2. Google Gemini (cloud fallback)
/// 3. Rule-based engine (deterministic final fallback)
/// Ensures zero-downtime, per-provider timeout handling, and records the origin provider.
/// </summary>
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
        var totalStopwatch = Stopwatch.StartNew();

        // Tier 1: Local AI model (Ollama)
        logger.LogInformation("Iniciando cadena de proveedores de IA para el atleta '{AthleteId}'. Paso 1: Ollama.", request.AthleteId);
        var tierStopwatch = Stopwatch.StartNew();
        RoutineSuggestionResponseDto? response = null;
        try
        {
            response = await ollamaClient.TrySuggestRoutineAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Excepción no controlada durante la consulta a Ollama tras {ElapsedMs} ms.", tierStopwatch.ElapsedMilliseconds);
        }
        finally
        {
            tierStopwatch.Stop();
        }

        if (response != null)
        {
            totalStopwatch.Stop();
            logger.LogInformation(
                "Propuesta de rutina resuelta por proveedor '{Provider}' en {ElapsedMs} ms (total: {TotalMs} ms).",
                response.ProviderUsed,
                tierStopwatch.ElapsedMilliseconds,
                totalStopwatch.ElapsedMilliseconds);
            return response;
        }

        // Tier 2: Cloud AI model as fallback (Google Gemini)
        logger.LogWarning(
            "El proveedor Ollama no estuvo disponible o superó su tiempo límite tras {ElapsedMs} ms. Paso 2: Activando respaldo con Google Gemini.",
            tierStopwatch.ElapsedMilliseconds);

        tierStopwatch.Restart();
        try
        {
            response = await geminiClient.TrySuggestRoutineAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Excepción no controlada durante la consulta a Gemini tras {ElapsedMs} ms.", tierStopwatch.ElapsedMilliseconds);
        }
        finally
        {
            tierStopwatch.Stop();
        }

        if (response != null)
        {
            totalStopwatch.Stop();
            logger.LogInformation(
                "Propuesta de rutina resuelta por proveedor '{Provider}' en {ElapsedMs} ms (total: {TotalMs} ms).",
                response.ProviderUsed,
                tierStopwatch.ElapsedMilliseconds,
                totalStopwatch.ElapsedMilliseconds);
            return response;
        }

        // Tier 3: Deterministic local rules fallback
        logger.LogWarning(
            "El proveedor Gemini no estuvo disponible, agotó cuota o superó su tiempo límite tras {ElapsedMs} ms. Paso 3: Activando respaldo final por reglas determinísticas locales.",
            tierStopwatch.ElapsedMilliseconds);

        tierStopwatch.Restart();
        RoutineSuggestionResponseDto? ruleResponse = null;
        try
        {
            ruleResponse = await ruleBasedClient.TrySuggestRoutineAsync(request, cancellationToken);
        }
        finally
        {
            tierStopwatch.Stop();
            totalStopwatch.Stop();
        }

        logger.LogInformation(
            "Propuesta de rutina resuelta por proveedor '{Provider}' en {ElapsedMs} ms (total: {TotalMs} ms).",
            ruleResponse?.ProviderUsed ?? "Reglas",
            tierStopwatch.ElapsedMilliseconds,
            totalStopwatch.ElapsedMilliseconds);

        return ruleResponse!;
    }
}
