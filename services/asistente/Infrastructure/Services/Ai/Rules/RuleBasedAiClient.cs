using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Ai.Rules;

public sealed class RuleBasedAiClient(ILogger<RuleBasedAiClient> logger) : IAiClient
{
    public string ProviderName => "Reglas";

    public Task<RoutineSuggestionResponseDto?> TrySuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Generando propuesta de entrenamiento mediante motor de reglas determinísticas para el objetivo: '{Goal}'.", request.Goal);

        var (title, justification, exercises) = DeterministicRuleEngine.GenerateProposal(
            request.Goal,
            request.MuscleGroupEmphasis);

        var response = new RoutineSuggestionResponseDto(
            AthleteId: request.AthleteId,
            ProposedTitle: title,
            JustificationSummary: justification,
            Exercises: exercises,
            ProviderUsed: ProviderName
        );

        return Task.FromResult<RoutineSuggestionResponseDto?>(response);
    }
}
