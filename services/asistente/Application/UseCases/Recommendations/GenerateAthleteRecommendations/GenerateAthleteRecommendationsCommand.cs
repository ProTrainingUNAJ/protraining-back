using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Application.UseCases.Recommendations.GenerateAthleteRecommendations;

public sealed record GenerateAthleteRecommendationsCommand(
    Guid AthleteId,
    Guid? CategoryId = null,
    int? Weeks = 4);

/// <summary>
/// Ejecuta el motor determinístico de recomendaciones para un atleta:
/// recopila perfil frente al grupo y tendencias, llama a la IA para redactar según KAN-378
/// y persiste las recomendaciones en estado Pending para revisión del PF (#196 · KAN-491).
/// </summary>
public sealed class GenerateAthleteRecommendationsCommandHandler(
    IStructureAthleteClient structureClient,
    ISeguimientoClient seguimientoClient,
    IAthleteRecommendationRepository recommendationRepository,
    IAiService aiService,
    IUnitOfWork unitOfWork,
    RecommendationsOptions? options = null,
    ILogger<GenerateAthleteRecommendationsCommandHandler>? logger = null)
{
    public const int DefaultWeeks = 4;
    public const int MinWeeks = 1;
    public const int MaxWeeks = 12;

    public async Task<RunAthleteRecommendationEngineResponseDto> HandleAsync(
        GenerateAthleteRecommendationsCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.AthleteId == Guid.Empty)
            throw new NotFoundException("El atleta no existe o no pertenece al preparador físico autenticado.");

        var weeks = command.Weeks ?? DefaultWeeks;
        if (weeks < MinWeeks || weeks > MaxWeeks)
            throw new DomainException($"La cantidad de semanas debe estar entre {MinWeeks} y {MaxWeeks}.");

        var isOwnAthlete = await structureClient.IsOwnAthleteAsync(command.AthleteId, cancellationToken);
        if (!isOwnAthlete)
            throw new NotFoundException("El atleta no existe o no pertenece al preparador físico autenticado.");

        var categories = await structureClient.GetAthleteCategoriesAsync(command.AthleteId, cancellationToken);
        StructureCategoryDto? targetCategory = null;

        if (command.CategoryId.HasValue && command.CategoryId.Value != Guid.Empty)
        {
            targetCategory = categories.FirstOrDefault(c => c.Id == command.CategoryId.Value)
                ?? throw new NotFoundException("La categoría solicitada no existe o no pertenece al atleta.");
        }
        else
        {
            targetCategory = categories.FirstOrDefault();
        }

        var testsTask = seguimientoClient.GetAthleteFitnessTestsAsync(command.AthleteId, cancellationToken);
        var averagesTask = targetCategory is not null
            ? seguimientoClient.GetCategoryAveragesAsync(targetCategory.Id, cancellationToken)
            : Task.FromResult<CategoryAveragesRemoteDto?>(null);
        var trendsTask = seguimientoClient.GetAthleteWeeklyTrendsAsync(command.AthleteId, weeks, cancellationToken);
        var existingTask = recommendationRepository.ListByAthleteIdsAndStatusesAsync(
            [command.AthleteId],
            RecommendationRecurrencePolicy.DeduplicationStatuses,
            cancellationToken);

        await Task.WhenAll(testsTask, averagesTask, trendsTask, existingTask);

        var athleteTests = await testsTask;
        var categoryAverages = await averagesTask;
        var weeklyTrends = await trendsTask;
        var existingRecommendations = await existingTask;
        var existingPending = existingRecommendations
            .Where(r => r.Status == RecommendationStatus.Pending)
            .ToList();

        var effectiveOptions = options ?? new RecommendationsOptions();
        var belowAverageThreshold = effectiveOptions.GetEffectiveBelowAverageThresholdPercent();
        var discardedTolerance = effectiveOptions.GetEffectiveDiscardedToleranceMeasurements();
        var analysis = AthleteProfileAnalyzer.Analyze(
            athleteTests,
            categoryAverages,
            weeklyTrends,
            belowAverageThreshold);
        var eligibleSignals = RecommendationRecurrencePolicy.FilterEligibleSignals(
            analysis.Signals,
            athleteTests,
            existingRecommendations,
            discardedTolerance);

        var newlyCreated = await RecommendationDraftGenerator.GenerateNewDraftsAsync(
            command.AthleteId,
            targetCategory?.Id,
            targetCategory?.Name,
            eligibleSignals,
            aiService,
            cancellationToken);

        foreach (var recommendation in newlyCreated)
        {
            await recommendationRepository.AddAsync(recommendation, cancellationToken);
        }

        if (newlyCreated.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var responseItems = newlyCreated
            .Concat(existingPending)
            .Select(r => RecommendationMapper.ToResponseDto(r, logger))
            .ToList();

        return new RunAthleteRecommendationEngineResponseDto(
            AthleteId: command.AthleteId,
            CategoryId: targetCategory?.Id,
            CategoryName: targetCategory?.Name,
            GeneratedCount: newlyCreated.Count,
            ExistingPendingCount: existingPending.Count,
            Recommendations: responseItems);
    }
}
