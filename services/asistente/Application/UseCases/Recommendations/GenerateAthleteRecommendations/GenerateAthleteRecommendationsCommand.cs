using Application.Common.Helpers;
using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;

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
    IIdentityHabitsRecommendationsClient identityClient,
    ISeguimientoClient seguimientoClient,
    IAthleteRecommendationRepository recommendationRepository,
    IAiService aiService,
    IUnitOfWork unitOfWork,
    RecommendationsOptions? options = null)
{
    public const int DefaultWeeks = 4;
    public const int MinWeeks = 1;
    public const int MaxWeeks = 12;

    private static readonly RecommendationStatus[] DeduplicationStatuses =
    [
        RecommendationStatus.Pending,
        RecommendationStatus.Discarded
    ];

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

        // KAN-493: no se genera ni persiste nada para un atleta con las recomendaciones deshabilitadas.
        // Se consulta una sola vez la preferencia del PF y se valida al tutor cuando corresponde.
        var pfEnabled = await identityClient.GetMyHabitsRecommendationsEnabledAsync(cancellationToken);
        if (pfEnabled is not true)
            return EmptyResult(command.AthleteId);

        var permission = await structureClient.GetHabitsPermissionAsync(command.AthleteId, cancellationToken);
        if (!HabitsRecommendationsPolicy.IsEnabled(pfEnabled, permission))
            return EmptyResult(command.AthleteId);

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
            DeduplicationStatuses,
            cancellationToken);

        await Task.WhenAll(testsTask, averagesTask, trendsTask, existingTask);

        var athleteTests = await testsTask;
        var categoryAverages = await averagesTask;
        var weeklyTrends = await trendsTask;
        var existingRecommendations = await existingTask;
        var existingPending = existingRecommendations
            .Where(r => r.Status == RecommendationStatus.Pending)
            .ToList();

        var belowAverageThreshold = (options ?? new RecommendationsOptions()).GetEffectiveBelowAverageThresholdPercent();
        var analysis = AthleteProfileAnalyzer.Analyze(
            athleteTests,
            categoryAverages,
            weeklyTrends,
            belowAverageThreshold);
        var existingReasons = existingRecommendations
            .Select(r => TextNormalizer.Normalize(r.Reason))
            .ToHashSet(StringComparer.Ordinal);

        var newlyCreated = new List<AthleteRecommendation>();

        foreach (var signal in analysis.Signals)
        {
            var normalizedReason = TextNormalizer.Normalize(signal.Reason);
            if (existingReasons.Contains(normalizedReason))
                continue;

            var aiRequest = RecommendationMapper.BuildTrainingAiRequest(
                command.AthleteId,
                targetCategory?.Name,
                signal);

            var drafted = await aiService.GenerateTrainingRecommendationAsync(aiRequest, cancellationToken);
            var recommendation = RecommendationMapper.CreatePendingTrainingEntity(
                command.AthleteId,
                targetCategory?.Id,
                targetCategory?.Name,
                signal,
                drafted);

            await recommendationRepository.AddAsync(recommendation, cancellationToken);
            newlyCreated.Add(recommendation);
            existingReasons.Add(normalizedReason);
        }

        if (newlyCreated.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var responseItems = newlyCreated
            .Concat(existingPending)
            .Select(RecommendationMapper.ToResponseDto)
            .ToList();

        return new RunAthleteRecommendationEngineResponseDto(
            AthleteId: command.AthleteId,
            CategoryId: targetCategory?.Id,
            CategoryName: targetCategory?.Name,
            GeneratedCount: newlyCreated.Count,
            ExistingPendingCount: existingPending.Count,
            Recommendations: responseItems);
    }

    private static RunAthleteRecommendationEngineResponseDto EmptyResult(Guid athleteId) =>
        new(
            AthleteId: athleteId,
            CategoryId: null,
            CategoryName: null,
            GeneratedCount: 0,
            ExistingPendingCount: 0,
            Recommendations: []);
}
