namespace Application.UseCases.Ai.SuggestRoutine;

using System.Text.Json;
using Application.DTOs.Ai;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Exceptions;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.Extensions.Logging;

public sealed class SuggestRoutineUseCase(
    IAiService aiService,
    IRoutineDraftRepository? routineDraftRepository = null,
    IUnitOfWork? unitOfWork = null,
    ILogger<SuggestRoutineUseCase>? logger = null,
    IAthleteAdaptationContextService? adaptationContextService = null,
    IStructureAthleteClient? structureAthleteClient = null)
{
    public async Task<RoutineSuggestionResponseDto> ExecuteAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.AthleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es obligatorio.");

        if (string.IsNullOrWhiteSpace(request.Goal))
            throw new DomainException("El objetivo de entrenamiento es obligatorio.");

        var normalizedRequest = request with { Goal = request.Goal.Trim() };

        var aiResponse = await aiService.SuggestRoutineAsync(normalizedRequest, cancellationToken);

        Guid? persistedDraftId = null;
        if (routineDraftRepository != null && unitOfWork != null)
        {
            try
            {
                var exercisesJson = JsonSerializer.Serialize(aiResponse.Exercises);
                var draft = RoutineDraft.Create(
                    athleteId: aiResponse.AthleteId,
                    goal: normalizedRequest.Goal,
                    proposedTitle: aiResponse.ProposedTitle,
                    justificationSummary: aiResponse.JustificationSummary,
                    providerUsed: aiResponse.ProviderUsed,
                    exercisesJson: exercisesJson
                );

                await routineDraftRepository.AddAsync(draft, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                persistedDraftId = draft.Id;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error al persistir el borrador de rutina generado por IA para el atleta {AthleteId}", request.AthleteId);
            }
        }

        return aiResponse with { Id = persistedDraftId ?? aiResponse.Id };
    }

    public async Task<RoutineSuggestionResponseDto> ExecuteAdaptationAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (adaptationContextService is null)
            throw new InvalidOperationException("El servicio de contexto de adaptación no está configurado.");
        if (structureAthleteClient is null)
            throw new InvalidOperationException("El cliente de estructura para validar ownership no está configurado.");

        if (request.AthleteId == Guid.Empty)
            throw new NotFoundException("El atleta no existe o no pertenece al preparador físico autenticado.");

        if (!await structureAthleteClient.IsOwnAthleteAsync(request.AthleteId, cancellationToken))
            throw new NotFoundException("El atleta no existe o no pertenece al preparador físico autenticado.");

        var context = await adaptationContextService.GetAsync(request.AthleteId, cancellationToken);
        var response = await ExecuteAsync(request with { AdaptationContext = context }, cancellationToken);
        var warnings = response.AdaptationWarnings is { Count: > 0 }
            ? response.AdaptationWarnings
            : context.Restrictions.Count == 0
                ? Array.Empty<string>()
                : new[] { "La propuesta fue validada contra las restricciones y sustitutos autorizados del atleta." };

        return response with
        {
            AdaptationWarnings = warnings
        };
    }
}
