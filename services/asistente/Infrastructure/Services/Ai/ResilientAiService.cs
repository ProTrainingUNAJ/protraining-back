using System.Diagnostics;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Infrastructure.Services.Ai.Gemini;
using Infrastructure.Services.Ai.Ollama;
using Infrastructure.Services.Ai.Rules;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Ai;

/// <summary>
/// Orquesta la cadena resiliente de proveedores de IA para el Asistente:
/// 1. Ollama (modelo local en Docker)
/// 2. Google Gemini (respaldo en la nube)
/// 3. Motor de reglas determinísticas locales (respaldo final garantizado)
/// Asegura alta disponibilidad, tiempos límites independientes por proveedor y registro del origen.
/// </summary>
public sealed class ResilientAiService(
    OllamaClient ollamaClient,
    GeminiClient geminiClient,
    RuleBasedAiClient ruleBasedClient,
    ILogger<ResilientAiService> logger) : IAiService
{
    private readonly IReadOnlyList<IAiClient> _providers = [ollamaClient, geminiClient, ruleBasedClient];

    public async Task<RoutineSuggestionResponseDto> SuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        logger.LogInformation("Iniciando cadena de proveedores de IA para el atleta '{AthleteId}'.", request.AthleteId);

        for (var i = 0; i < _providers.Count; i++)
        {
            var provider = _providers[i];
            var isLast = i == _providers.Count - 1;
            var tierNumber = i + 1;

            logger.LogInformation(
                "Paso {Step} de {Total}: Intentando con proveedor '{Provider}'.",
                tierNumber,
                _providers.Count,
                provider.ProviderName);

            var tierStopwatch = Stopwatch.StartNew();
            RoutineSuggestionResponseDto? response = null;

            try
            {
                response = await provider.TrySuggestRoutineAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Excepción no controlada durante la consulta a {Provider} tras {ElapsedMs} ms.",
                    provider.ProviderName,
                    tierStopwatch.ElapsedMilliseconds);
            }
            finally
            {
                tierStopwatch.Stop();
            }

            if (response != null)
            {
                totalStopwatch.Stop();
                logger.LogInformation(
                    "Propuesta de rutina resuelta exitosamente por proveedor '{Provider}' en {ElapsedMs} ms (tiempo total: {TotalMs} ms).",
                    response.ProviderUsed,
                    tierStopwatch.ElapsedMilliseconds,
                    totalStopwatch.ElapsedMilliseconds);
                return response;
            }

            if (!isLast)
            {
                logger.LogWarning(
                    "El proveedor '{Provider}' no estuvo disponible, agotó cuota o superó su tiempo límite tras {ElapsedMs} ms. Activando siguiente respaldo.",
                    provider.ProviderName,
                    tierStopwatch.ElapsedMilliseconds);
            }
        }

        totalStopwatch.Stop();
        throw new InvalidOperationException("Ningún proveedor de la cadena resiliente pudo generar la rutina.");
    }
}
