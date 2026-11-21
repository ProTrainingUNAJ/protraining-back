using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Recommendations.GenerateAthleteRecommendations;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Application.UseCases.Recommendations.GenerateCategoryRecommendations;

public sealed record GenerateCategoryRecommendationsCommand(
    Guid CategoryId,
    int? Weeks = 4);

/// <summary>
/// Ejecuta el motor determinístico de recomendaciones sobre todos los atletas activos de una categoría:
/// consulta promedios de categoría una sola vez, analiza perfil y tendencias en paralelo con límite de concurrencia,
/// redacta vía IA sin PII y persiste secuencialmente las nuevas recomendaciones en estado Pending (#196 · KAN-491).
/// </summary>
public sealed class GenerateCategoryRecommendationsCommandHandler(
    IStructureAthleteClient structureClient,
    IIdentityHabitsRecommendationsClient identityClient,
    ISeguimientoClient seguimientoClient,
    IAthleteRecommendationRepository recommendationRepository,
    IScopedAiServiceRunner scopedAiServiceRunner,
    IUnitOfWork unitOfWork,
    RecommendationsOptions? options = null,
    ILogger<GenerateCategoryRecommendationsCommandHandler>? logger = null)
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
        var existingTask = recommendationRepository.ListByAthleteIdsAndStatusesAsync(
            athleteIds,
            RecommendationRecurrencePolicy.DeduplicationStatuses,
            cancellationToken);
        await Task.WhenAll(averagesTask, existingTask);

        var categoryAverages = await averagesTask;
        var existingRecommendations = await existingTask;
        var existingPending = existingRecommendations
            .Where(r => r.Status == RecommendationStatus.Pending)
            .ToList();

        var existingByAthlete = existingRecommendations
            .GroupBy(r => r.AthleteId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AthleteRecommendation>)g.ToList());

        var effectiveOptions = options ?? new RecommendationsOptions();
        var belowAverageThreshold = effectiveOptions.GetEffectiveBelowAverageThresholdPercent();
        var discardedTolerance = effectiveOptions.GetEffectiveDiscardedToleranceMeasurements();
        var maxParallelism = effectiveOptions.GetEffectiveMaxCategoryParallelism();

        // Fase 1 (concurrente con límite): consultar permisos, métricas y redactar borradores en memoria.
        // Cada atleta resuelve IAiService en un scope aislado (vía IScopedAiServiceRunner) para evitar
        // acceso concurrente a dependencias scoped (como DbContext en auditoría ai_logs).
        using var concurrencyGate = new SemaphoreSlim(maxParallelism, maxParallelism);
        var athleteTasks = athleteIds.Select(async athleteId =>
        {
            await concurrencyGate.WaitAsync(cancellationToken);
            try
            {
                var permission = await structureClient.GetHabitsPermissionAsync(athleteId, cancellationToken);
                if (!HabitsRecommendationsPolicy.IsEnabled(pfEnabled, permission))
                    return (Evaluated: false, Created: (IReadOnlyList<AthleteRecommendation>)[]);

                var testsTask = seguimientoClient.GetAthleteFitnessTestsAsync(athleteId, cancellationToken);
                var trendsTask = seguimientoClient.GetAthleteWeeklyTrendsAsync(athleteId, weeks, cancellationToken);
                await Task.WhenAll(testsTask, trendsTask);

                var athleteTests = await testsTask;
                var analysis = AthleteProfileAnalyzer.Analyze(
                    athleteTests,
                    categoryAverages,
                    await trendsTask,
                    belowAverageThreshold);

                if (analysis.Signals.Count == 0)
                    return (Evaluated: true, Created: (IReadOnlyList<AthleteRecommendation>)[]);

                var athleteExisting = existingByAthlete.TryGetValue(athleteId, out var history)
                    ? history
                    : [];

                var eligibleSignals = RecommendationRecurrencePolicy.FilterEligibleSignals(
                    analysis.Signals,
                    athleteTests,
                    athleteExisting,
                    discardedTolerance);

                if (eligibleSignals.Count == 0)
                    return (Evaluated: true, Created: (IReadOnlyList<AthleteRecommendation>)[]);

                var createdForAthlete = await scopedAiServiceRunner.ExecuteAsync(
                    (scopedAiService, ct) => RecommendationDraftGenerator.GenerateNewDraftsAsync(
                        athleteId,
                        category.Id,
                        category.Name,
                        eligibleSignals,
                        scopedAiService,
                        ct),
                    cancellationToken);

                return (Evaluated: true, Created: createdForAthlete);
            }
            finally
            {
                concurrencyGate.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(athleteTasks);

        // Fase 2 (secuencial): agregar entidades al repositorio y persistir en una única transacción.
        var newlyCreated = new List<AthleteRecommendation>();
        var evaluatedAthletes = 0;

        foreach (var result in results)
        {
            if (result.Evaluated)
                evaluatedAthletes++;

            foreach (var recommendation in result.Created)
            {
                await recommendationRepository.AddAsync(recommendation, cancellationToken);
                newlyCreated.Add(recommendation);
            }
        }

        if (newlyCreated.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var responseItems = newlyCreated
            .Concat(existingPending)
            .Select(r => RecommendationMapper.ToResponseDto(r, logger))
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
