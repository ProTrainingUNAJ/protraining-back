using Application.Common.Helpers;
using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Recommendations.GenerateAthleteRecommendations;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.Recommendations.GenerateCategoryRecommendations;

public sealed record GenerateCategoryRecommendationsCommand(
    Guid CategoryId,
    int? Weeks = 4);

/// <summary>
/// Ejecuta el motor determinístico de recomendaciones sobre todos los atletas activos de una categoría:
/// consulta promedios de categoría una sola vez, analiza perfil y tendencias de cada atleta,
/// redacta vía IA sin PII y persiste las nuevas recomendaciones en estado Pending (#196 · KAN-491).
/// </summary>
public sealed class GenerateCategoryRecommendationsCommandHandler(
    IStructureAthleteClient structureClient,
    IIdentityHabitsRecommendationsClient identityClient,
    ISeguimientoClient seguimientoClient,
    IAthleteRecommendationRepository recommendationRepository,
    IAiService aiService,
    IUnitOfWork unitOfWork)
{
    public async Task<RunCategoryRecommendationEngineResponseDto> HandleAsync(
        GenerateCategoryRecommendationsCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.CategoryId == Guid.Empty)
            throw new NotFoundException("La categoría no existe o no pertenece al preparador físico autenticado.");

        var weeks = command.Weeks ?? GenerateAthleteRecommendationsCommandHandler.DefaultWeeks;
        if (weeks < GenerateAthleteRecommendationsCommandHandler.MinWeeks ||
            weeks > GenerateAthleteRecommendationsCommandHandler.MaxWeeks)
        {
            throw new DomainException(
                $"La cantidad de semanas debe estar entre {GenerateAthleteRecommendationsCommandHandler.MinWeeks} y {GenerateAthleteRecommendationsCommandHandler.MaxWeeks}.");
        }

        var category = await structureClient.GetCategoryAsync(command.CategoryId, cancellationToken)
            ?? throw new NotFoundException("La categoría no existe o no pertenece al preparador físico autenticado.");

        // KAN-493: la preferencia del PF se consulta una sola vez para toda la categoría.
        // Con la opción desactivada (o Identidad no disponible) no se analiza ningún atleta.
        var pfEnabled = await identityClient.GetMyHabitsRecommendationsEnabledAsync(cancellationToken);
        if (pfEnabled is not true)
            return EmptyResult(category.Id, category.Name);

        var categoryAthletes = await structureClient.GetCategoryAthletesAsync(command.CategoryId, cancellationToken);
        var athleteIds = categoryAthletes
            .Select(a => a.Id)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (athleteIds.Count == 0)
        {
            return new RunCategoryRecommendationEngineResponseDto(
                CategoryId: category.Id,
                CategoryName: category.Name,
                EvaluatedAthletes: 0,
                GeneratedCount: 0,
                ExistingPendingCount: 0,
                Recommendations: []);
        }

        var averagesTask = seguimientoClient.GetCategoryAveragesAsync(category.Id, cancellationToken);
        var pendingTask = recommendationRepository.ListPendingByAthleteIdsAsync(athleteIds, cancellationToken);
        await Task.WhenAll(averagesTask, pendingTask);

        var categoryAverages = await averagesTask;
        var existingPending = await pendingTask;

        var pendingReasonsByAthlete = existingPending
            .GroupBy(r => r.AthleteId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => TextNormalizer.Normalize(r.Reason)).ToHashSet(StringComparer.Ordinal));

        var newlyCreated = new List<AthleteRecommendation>();
        var evaluatedAthletes = 0;

        foreach (var athleteId in athleteIds)
        {
            var permission = await structureClient.GetHabitsPermissionAsync(athleteId, cancellationToken);
            if (!HabitsRecommendationsPolicy.IsEnabled(pfEnabled, permission))
                continue;

            evaluatedAthletes++;
            var testsTask = seguimientoClient.GetAthleteFitnessTestsAsync(athleteId, cancellationToken);
            var trendsTask = seguimientoClient.GetAthleteWeeklyTrendsAsync(athleteId, weeks, cancellationToken);
            await Task.WhenAll(testsTask, trendsTask);

            var analysis = AthleteProfileAnalyzer.Analyze(
                await testsTask,
                categoryAverages,
                await trendsTask);

            if (analysis.Signals.Count == 0)
                continue;

            if (!pendingReasonsByAthlete.TryGetValue(athleteId, out var existingReasons))
            {
                existingReasons = new HashSet<string>(StringComparer.Ordinal);
                pendingReasonsByAthlete[athleteId] = existingReasons;
            }

            foreach (var signal in analysis.Signals)
            {
                var normalizedReason = TextNormalizer.Normalize(signal.Reason);
                if (existingReasons.Contains(normalizedReason))
                    continue;

                var aiRequest = RecommendationMapper.BuildTrainingAiRequest(
                    athleteId,
                    category.Name,
                    signal);

                var drafted = await aiService.GenerateTrainingRecommendationAsync(aiRequest, cancellationToken);
                var recommendation = RecommendationMapper.CreatePendingTrainingEntity(
                    athleteId,
                    category.Id,
                    category.Name,
                    signal,
                    drafted);

                await recommendationRepository.AddAsync(recommendation, cancellationToken);
                newlyCreated.Add(recommendation);
                existingReasons.Add(normalizedReason);
            }
        }

        if (newlyCreated.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var responseItems = newlyCreated
            .Concat(existingPending)
            .Select(RecommendationMapper.ToResponseDto)
            .ToList();

        return new RunCategoryRecommendationEngineResponseDto(
            CategoryId: category.Id,
            CategoryName: category.Name,
            EvaluatedAthletes: evaluatedAthletes,
            GeneratedCount: newlyCreated.Count,
            ExistingPendingCount: existingPending.Count,
            Recommendations: responseItems);
    }

    private static RunCategoryRecommendationEngineResponseDto EmptyResult(Guid categoryId, string categoryName) =>
        new(
            CategoryId: categoryId,
            CategoryName: categoryName,
            EvaluatedAthletes: 0,
            GeneratedCount: 0,
            ExistingPendingCount: 0,
            Recommendations: []);
}
