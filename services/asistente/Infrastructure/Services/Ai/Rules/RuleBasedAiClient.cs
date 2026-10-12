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

        var exercises = DetermineExercisesByRules(request.Goal, request.MuscleGroupEmphasis);

        var response = new RoutineSuggestionResponseDto(
            AthleteId: request.AthleteId,
            ProposedTitle: $"Plan de Entrenamiento Estructurado ({request.Goal})",
            JustificationSummary: "Propuesta generada mediante motor de reglas determinísticas locales para garantizar la continuidad del entrenamiento sin depender de servicios de IA externos.",
            Exercises: exercises,
            ProviderUsed: ProviderName
        );

        return Task.FromResult<RoutineSuggestionResponseDto?>(response);
    }

    private static List<SuggestedExerciseDto> DetermineExercisesByRules(string goal, List<string>? emphasis)
    {
        var lowerGoal = goal.ToLowerInvariant();
        var hasLegs = emphasis?.Any(m => m.Contains("pierna", StringComparison.OrdinalIgnoreCase) || m.Contains("cadera", StringComparison.OrdinalIgnoreCase)) ?? false;

        if (lowerGoal.Contains("pierna") || hasLegs)
        {
            return
            [
                new SuggestedExerciseDto("Sentadilla Goblet con mancuerna", 4, 8, 70.0m),
                new SuggestedExerciseDto("Peso muerto rumano con mancuernas", 3, 10, 65.0m),
                new SuggestedExerciseDto("Zancadas alternadas", 3, 10, 60.0m),
                new SuggestedExerciseDto("Plancha isométrica frontal", 3, 30, 0.0m)
            ];
        }

        if (lowerGoal.Contains("fuerza"))
        {
            return
            [
                new SuggestedExerciseDto("Sentadilla trasera con barra", 4, 6, 80.0m),
                new SuggestedExerciseDto("Press de banca plano con barra", 4, 6, 80.0m),
                new SuggestedExerciseDto("Remo con barra", 3, 8, 75.0m),
                new SuggestedExerciseDto("Press militar de pie", 3, 8, 70.0m)
            ];
        }

        // General physical conditioning routine
        return
        [
            new SuggestedExerciseDto("Sentadilla con peso corporal / Goblet", 3, 10, 65.0m),
            new SuggestedExerciseDto("Press de banca con mancuernas", 3, 10, 65.0m),
            new SuggestedExerciseDto("Remo sentado en polea", 3, 10, 65.0m),
            new SuggestedExerciseDto("Puente de glúteos", 3, 12, 0.0m)
        ];
    }
}
