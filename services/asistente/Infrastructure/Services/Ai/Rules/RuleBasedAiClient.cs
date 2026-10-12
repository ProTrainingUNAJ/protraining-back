using System.Diagnostics;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Ai.Rules;

public sealed class RuleBasedAiClient(
    IAiAuditService aiAuditService,
    ILogger<RuleBasedAiClient> logger) : IAiClient
{
    public string ProviderName => "Reglas";

    public async Task<RoutineSuggestionResponseDto?> TrySuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Generando propuesta de entrenamiento mediante motor de reglas determinísticas para el objetivo: '{Goal}'.", request.Goal);

        var (title, justification, exercises) = DeterministicRuleEngine.GenerateProposal(
            request.Goal,
            request.MuscleGroupEmphasis);

        stopwatch.Stop();

        var prompt = $"Objetivo: {request.Goal}. Énfasis muscular: {(request.MuscleGroupEmphasis is { Count: > 0 } ? string.Join(", ", request.MuscleGroupEmphasis) : "General")}";

        var response = new RoutineSuggestionResponseDto(
            AthleteId: request.AthleteId,
            ProposedTitle: title,
            JustificationSummary: justification,
            Exercises: exercises,
            ProviderUsed: ProviderName
        );

        await aiAuditService.LogAsync(AiLog.Create(
            ProviderName,
            prompt,
            stopwatch.ElapsedMilliseconds,
            isValid: true,
            validationResult: "Valido",
            response: title,
            athleteId: request.AthleteId,
            promptTokens: 0,
            completionTokens: 0,
            totalTokens: 0));

        return response;
    }
}
