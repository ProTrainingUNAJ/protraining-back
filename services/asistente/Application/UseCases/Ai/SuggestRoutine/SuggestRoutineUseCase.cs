namespace Application.UseCases.Ai.SuggestRoutine;

using System.Text.Json;
using Application.Common.PfDecisions;
using Application.DTOs.Adaptation;
using Application.DTOs.Ai;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Exceptions;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.Extensions.Logging;

public sealed class SuggestRoutineUseCase(
    IAiService aiService,
    IAthleteAdaptationContextService adaptationContextService,
    IStructureAthleteClient structureAthleteClient,
    IRoutineDraftRepository? routineDraftRepository = null,
    IUnitOfWork? unitOfWork = null,
    ILogger<SuggestRoutineUseCase>? logger = null,
    IPfDecisionRepository? pfDecisionRepository = null,
    ITrainingPlanServiceClient? trainingPlanServiceClient = null)
{
    public async Task<RoutineSuggestionResponseDto> ExecuteAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.AthleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es obligatorio.");

        if (string.IsNullOrWhiteSpace(request.Goal))
            throw new DomainException("El objetivo de entrenamiento es obligatorio.");

        var previousDecisions = request.PreviousPfDecisions;
        if (previousDecisions is null && pfDecisionRepository != null)
        {
            var decisions = await pfDecisionRepository.GetRecentAsync(
                request.AthleteId,
                cancellationToken: cancellationToken);
            previousDecisions = PfDecisionContextMapper.Map(decisions);
        }

        var normalizedRequest = request with
        {
            Goal = request.Goal.Trim(),
            PreviousPfDecisions = previousDecisions
        };

        var aiResponse = await aiService.SuggestRoutineAsync(normalizedRequest, cancellationToken);

        var persistedDraftId = await TryPersistDraftAsync(
            aiResponse.AthleteId,
            normalizedRequest.Goal,
            aiResponse.ProposedTitle,
            aiResponse.JustificationSummary,
            aiResponse.ProviderUsed,
            aiResponse.Exercises,
            cancellationToken);

        return aiResponse with { Id = persistedDraftId ?? aiResponse.Id };
    }

    public async Task<RoutineSuggestionResponseDto> ExecuteAdaptationAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.AthleteId == Guid.Empty)
            throw new NotFoundException("El atleta no existe o no pertenece al preparador físico autenticado.");

        if (!await structureAthleteClient.IsOwnAthleteAsync(request.AthleteId, cancellationToken))
            throw new NotFoundException("El atleta no existe o no pertenece al preparador físico autenticado.");

        var context = await adaptationContextService.GetAsync(request.AthleteId, cancellationToken);

        if (request.BaseExercises is { Count: > 0 } baseExercises)
        {
            context = await EnrichContextForBaseExercisesAsync(context, baseExercises, cancellationToken);
            return await BuildAdaptedResponseFromBaseRoutineAsync(request, baseExercises, context, cancellationToken);
        }

        var response = await ExecuteAsync(request with { AdaptationContext = context }, cancellationToken);
        var warnings = response.AdaptationWarnings is { Count: > 0 }
            ? response.AdaptationWarnings
            : context.Restrictions.Count == 0
                ? Array.Empty<string>()
                : new[] { "La propuesta fue validada contra las restricciones y sustitutos autorizados del atleta." };

        return response with
        {
            BaseExercises = response.BaseExercises ?? response.Exercises,
            AdaptationWarnings = warnings
        };
    }

    private async Task<AthleteAdaptationContextDto> EnrichContextForBaseExercisesAsync(
        AthleteAdaptationContextDto context,
        IReadOnlyList<SuggestedExerciseDto> baseExercises,
        CancellationToken cancellationToken)
    {
        if (trainingPlanServiceClient is null || context.ActiveInjuries.Count == 0)
            return context;

        var existingRestrictedIds = context.Restrictions
            .Select(r => r.ExerciseId)
            .ToHashSet();

        var missingExerciseIds = baseExercises
            .Where(e => e.ExerciseId.HasValue && e.ExerciseId.Value != Guid.Empty && !existingRestrictedIds.Contains(e.ExerciseId.Value))
            .Select(e => e.ExerciseId!.Value)
            .Distinct()
            .ToList();

        if (missingExerciseIds.Count == 0)
            return context;

        var expandedZones = ExpandInjuryZones(context.ActiveInjuries);
        if (expandedZones.Count == 0)
            return context;

        var painZonesParam = string.Join(",", expandedZones.Select(z => $"{z}:3"));
        var checkedRestrictions = await trainingPlanServiceClient.CheckExerciseRestrictionsAsync(
            painZonesParam,
            missingExerciseIds,
            cancellationToken);

        var primaryInjuryZone = context.ActiveInjuries
            .Select(i => i.Zone?.Trim())
            .FirstOrDefault(z => !string.IsNullOrWhiteSpace(z));

        var additionalRestrictions = checkedRestrictions
            .Where(r => r.Restricted && r.Substitutes is { Count: > 0 })
            .Select(r =>
            {
                var displayZone = ResolveDisplayZone(primaryInjuryZone, r.Zone);
                return new ExerciseRestrictionContextDto(
                    r.ExerciseId,
                    displayZone,
                    r.Reason,
                    r.Substitutes
                        .Where(s => s.ExerciseId != Guid.Empty)
                        .Select(s => new AuthorizedSubstituteDto(s.ExerciseId, s.Name))
                        .ToList());
            })
            .Where(r => r.Substitutes.Count > 0)
            .ToList();

        if (additionalRestrictions.Count == 0)
            return context;

        var merged = context.Restrictions.Concat(additionalRestrictions).ToList();
        return context with { Restrictions = merged };
    }

    private static IReadOnlyList<string> ExpandInjuryZones(IReadOnlyList<ActiveInjuryRemoteDto> activeInjuries)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddZone(string? zone)
        {
            if (string.IsNullOrWhiteSpace(zone))
                return;
            var trimmed = zone.Trim().ToLowerInvariant();
            if (seen.Add(trimmed))
                result.Add(trimmed);
        }

        foreach (var injury in activeInjuries)
        {
            var raw = injury.Zone?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            AddZone(raw);

            switch (raw)
            {
                case "rodilla" or "knee":
                    AddZone("cuadriceps");
                    AddZone("isquiotibiales");
                    AddZone("gemelos");
                    break;
                case "isquiotibiales" or "isquiotibial":
                    AddZone("isquiotibiales");
                    AddZone("cuadriceps");
                    break;
                case "cuadriceps" or "cuádriceps":
                    AddZone("cuadriceps");
                    break;
                case "hombro" or "shoulder" or "hombros":
                    AddZone("hombros");
                    break;
                case "espalda" or "back" or "espalda_baja" or "lumbar":
                    AddZone("espalda_baja");
                    AddZone("espalda_media");
                    AddZone("espalda_alta");
                    break;
            }
        }

        return result;
    }

    private static string? ResolveDisplayZone(string? primaryInjuryZone, string? matchedRestrictionZone)
    {
        if (!string.IsNullOrWhiteSpace(primaryInjuryZone) &&
            (string.Equals(primaryInjuryZone, "rodilla", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(primaryInjuryZone, "knee", StringComparison.OrdinalIgnoreCase)))
        {
            return "rodilla";
        }

        return !string.IsNullOrWhiteSpace(matchedRestrictionZone)
            ? matchedRestrictionZone
            : primaryInjuryZone;
    }

    private async Task<RoutineSuggestionResponseDto> BuildAdaptedResponseFromBaseRoutineAsync(
        RoutineSuggestionRequestDto request,
        IReadOnlyList<SuggestedExerciseDto> baseExercises,
        AthleteAdaptationContextDto context,
        CancellationToken cancellationToken)
    {
        var restrictionsByExercise = context.Restrictions
            .Where(r => r.Substitutes.Count > 0)
            .GroupBy(r => r.ExerciseId)
            .ToDictionary(g => g.Key, g => g.First());

        var adaptedExercises = new List<SuggestedExerciseDto>(baseExercises.Count);
        var changes = new List<AdaptationChangeDto>();
        var warnings = new List<string>();

        foreach (var baseEx in baseExercises)
        {
            if (baseEx.ExerciseId is { } exId &&
                exId != Guid.Empty &&
                restrictionsByExercise.TryGetValue(exId, out var restriction))
            {
                var substitute = restriction.Substitutes[0];
                var adapted = baseEx with
                {
                    ExerciseId = substitute.ExerciseId,
                    ExerciseName = substitute.Name,
                    IsReference = false
                };
                adaptedExercises.Add(adapted);

                var reason = BuildAdaptationReason(restriction, substitute.Name);
                warnings.Add(reason);
                changes.Add(new AdaptationChangeDto(
                    baseEx.ExerciseId,
                    baseEx.ExerciseName,
                    substitute.ExerciseId,
                    substitute.Name,
                    restriction.Zone ?? string.Empty,
                    reason,
                    restriction.Substitutes));
            }
            else
            {
                adaptedExercises.Add(baseEx);
            }
        }

        if (warnings.Count == 0 && context.Restrictions.Count > 0)
        {
            warnings.Add("La propuesta fue validada contra las restricciones y sustitutos autorizados del atleta.");
        }

        var goalText = string.IsNullOrWhiteSpace(request.Goal)
            ? "Adaptación individual de rutina base"
            : request.Goal.Trim();

        var justification = changes.Count > 0
            ? string.Join(" ", changes.Select(c => $"Se reemplaza {c.OriginalExerciseName} por {c.FinalExerciseName}: {c.Reason}"))
            : context.ActiveInjuries.Count > 0
                ? "La rutina base no presenta ejercicios contraindicados para las lesiones activas del atleta."
                : "El atleta no registra lesiones activas que requieran modificar la rutina base de la categoría.";

        var persistedDraftId = await TryPersistDraftAsync(
            request.AthleteId,
            goalText,
            goalText,
            justification,
            "Reglas",
            adaptedExercises,
            cancellationToken);

        return new RoutineSuggestionResponseDto(
            AthleteId: request.AthleteId,
            ProposedTitle: goalText,
            JustificationSummary: justification,
            Exercises: adaptedExercises,
            ProviderUsed: "Reglas",
            Id: persistedDraftId,
            WasAdapted: changes.Count > 0,
            AdaptationWarnings: warnings,
            AdaptationChanges: changes,
            BaseExercises: baseExercises.ToList());
    }

    private static string BuildAdaptationReason(
        ExerciseRestrictionContextDto restriction,
        string substituteName)
    {
        var zone = string.IsNullOrWhiteSpace(restriction.Zone) ? "zona afectada" : restriction.Zone.Trim();
        var normalizedReason = NormalizeReason(zone, restriction.Reason);
        return $"Sustitución biomecánica por lesión de {zone} para {normalizedReason}. Sustituto autorizado: {substituteName}.";
    }

    private static string NormalizeReason(string zone, string? reason)
    {
        if (string.Equals(zone, "rodilla", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(reason) &&
            reason.Contains("rotul", StringComparison.OrdinalIgnoreCase))
        {
            return "reducir estrés en tendón rotuliano";
        }

        var normalized = string.IsNullOrWhiteSpace(reason)
            ? "reducir el estrés de la zona lesionada"
            : reason.Trim().TrimEnd('.');
        return char.ToLowerInvariant(normalized[0]) + normalized[1..];
    }

    private async Task<Guid?> TryPersistDraftAsync(
        Guid athleteId,
        string goal,
        string proposedTitle,
        string justificationSummary,
        string providerUsed,
        IReadOnlyList<SuggestedExerciseDto> exercises,
        CancellationToken cancellationToken)
    {
        if (routineDraftRepository is null || unitOfWork is null)
            return null;

        try
        {
            var exercisesJson = JsonSerializer.Serialize(exercises);
            var draft = RoutineDraft.Create(
                athleteId: athleteId,
                goal: goal,
                proposedTitle: proposedTitle,
                justificationSummary: justificationSummary,
                providerUsed: providerUsed,
                exercisesJson: exercisesJson);

            await routineDraftRepository.AddAsync(draft, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return draft.Id;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error al persistir el borrador de rutina generado por IA para el atleta {AthleteId}", athleteId);
            return null;
        }
    }
}
