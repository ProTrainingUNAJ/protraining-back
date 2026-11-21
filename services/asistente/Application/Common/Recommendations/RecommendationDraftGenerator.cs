using Application.Common.Helpers;
using Application.Interfaces.Services;
using Domain.Entities;

namespace Application.Common.Recommendations;

/// <summary>
/// Genera entidades de recomendación pendientes a partir de señales analíticas ya detectadas,
/// deduplicando por motivo normalizado y redactando vía IA sin PII (KAN-491).
/// No accede al repositorio ni a IUnitOfWork, pero invoca a IAiService (que audita en ai_logs);
/// por lo tanto, en ejecuciones paralelas por atleta debe invocarse con un scope/IAiService aislado por tarea.
/// </summary>
public static class RecommendationDraftGenerator
{
    public static async Task<IReadOnlyList<AthleteRecommendation>> GenerateNewDraftsAsync(
        Guid athleteId,
        Guid? categoryId,
        string? categoryName,
        IReadOnlyList<RecommendationSignal> signals,
        ISet<string> existingReasons,
        IAiService aiService,
        CancellationToken cancellationToken = default)
    {
        if (signals.Count == 0)
            return [];

        var created = new List<AthleteRecommendation>(signals.Count);
        var seenReasons = new HashSet<string>(existingReasons, StringComparer.Ordinal);

        foreach (var signal in signals)
        {
            var normalizedReason = TextNormalizer.Normalize(signal.Reason);
            if (!seenReasons.Add(normalizedReason))
                continue;

            var aiRequest = RecommendationMapper.BuildTrainingAiRequest(
                athleteId,
                categoryName,
                signal);

            var drafted = await aiService.GenerateTrainingRecommendationAsync(aiRequest, cancellationToken);
            var recommendation = RecommendationMapper.CreatePendingTrainingEntity(
                athleteId,
                categoryId,
                categoryName,
                signal,
                drafted);

            created.Add(recommendation);
        }

        return created;
    }
}
