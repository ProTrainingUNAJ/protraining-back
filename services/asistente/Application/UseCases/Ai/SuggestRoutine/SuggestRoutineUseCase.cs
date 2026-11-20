namespace Application.UseCases.Ai.SuggestRoutine;

using System.Text.Json;
using Application.Common.Adaptation;
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
    ITrainingPlanServiceClient? trainingPlanServiceClient = null,
    AdaptationOptions? adaptationOptions = null)
{
    public async Task<RoutineSuggestionResponseDto> ExecuteAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.AthleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es obligatorio.");

        var normalizedRequest = await BuildNormalizedRequestAsync(request, cancellationToken);

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
            context = await EnrichContextForExercisesAsync(context, baseExercises, cancellationToken);
            return await BuildAdaptedResponseFromBaseRoutineAsync(request, baseExercises, context, cancellationToken);
        }

        var normalizedRequest = (await BuildNormalizedRequestAsync(request, cancellationToken)) with
        {
            AdaptationContext = context
        };

        var aiResponse = await aiService.SuggestRoutineAsync(normalizedRequest, cancellationToken);

        // Post-chequeo biomecánico sobre los ejercicios propuestos por la IA (por si propuso ejercicios sin récord 1RM previo)
        if (trainingPlanServiceClient is not null &&
            context.ActiveInjuries.Count > 0 &&
            aiResponse.Exercises.Count > 0)
        {
            var enrichedContext = await EnrichContextForExercisesAsync(context, aiResponse.Exercises, cancellationToken);
            if (enrichedContext.Restrictions.Count > context.Restrictions.Count)
            {
                context = enrichedContext;
                aiResponse = ApplyAdditionalRestrictions(aiResponse, context);
            }
        }

        var persistedDraftId = await TryPersistDraftAsync(
            aiResponse.AthleteId,
            normalizedRequest.Goal,
            aiResponse.ProposedTitle,
            aiResponse.JustificationSummary,
            aiResponse.ProviderUsed,
            aiResponse.Exercises,
            cancellationToken);

        var baseWarningList = aiResponse.AdaptationWarnings is { Count: > 0 }
            ? aiResponse.AdaptationWarnings.ToList()
            : context.Restrictions.Count == 0
                ? new List<string>()
                : new List<string> { "La propuesta fue validada contra las restricciones y sustitutos autorizados del atleta." };

        var conservativePercent = ResolveConservativePercent(context);
        foreach (var estimatedWarning in BuildEstimatedAndMissingRecordWarnings(
                     context.CurrentRecords,
                     aiResponse.BaseExercises ?? aiResponse.Exercises,
                     aiResponse.Exercises,
                     conservativePercent))
        {
            if (!baseWarningList.Contains(estimatedWarning))
                baseWarningList.Add(estimatedWarning);
        }

        return aiResponse with
        {
            Id = persistedDraftId ?? aiResponse.Id,
            BaseExercises = aiResponse.BaseExercises ?? aiResponse.Exercises,
            AdaptationWarnings = baseWarningList
        };
    }

    private async Task<RoutineSuggestionRequestDto> BuildNormalizedRequestAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken)
    {
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

        return request with
        {
            Goal = request.Goal.Trim(),
            PreviousPfDecisions = previousDecisions
        };
    }

    private static RoutineSuggestionResponseDto ApplyAdditionalRestrictions(
        RoutineSuggestionResponseDto response,
        AthleteAdaptationContextDto context)
    {
        var (updatedExercises, changes, warnings, adaptedAny) = AuthorizedSubstituteHelper.AdaptRestrictedExercises(
            response.Exercises,
            context,
            response.AdaptationChanges,
            response.AdaptationWarnings);

        if (!adaptedAny)
            return response;

        return response with
        {
            Exercises = updatedExercises,
            BaseExercises = response.BaseExercises ?? response.Exercises.ToList(),
            WasAdapted = true,
            AdaptationWarnings = warnings,
            AdaptationChanges = changes
        };
    }

    private async Task<AthleteAdaptationContextDto> EnrichContextForExercisesAsync(
        AthleteAdaptationContextDto context,
        IReadOnlyList<SuggestedExerciseDto> targetExercises,
        CancellationToken cancellationToken)
    {
        if (trainingPlanServiceClient is null || context.ActiveInjuries.Count == 0)
            return context;

        var existingRestrictedIds = context.Restrictions
            .Select(r => r.ExerciseId)
            .ToHashSet();

        var missingExerciseIds = targetExercises
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
        var (adaptedExercises, changes, warnings, _) = AuthorizedSubstituteHelper.AdaptRestrictedExercises(baseExercises, context);

        if (warnings.Count == 0 && context.Restrictions.Count > 0)
        {
            warnings.Add("La propuesta fue validada contra las restricciones y sustitutos autorizados del atleta.");
        }

        var conservativePercent = ResolveConservativePercent(context);
        foreach (var recordWarning in BuildEstimatedAndMissingRecordWarnings(
                     context.CurrentRecords,
                     baseExercises,
                     adaptedExercises,
                     conservativePercent))
        {
            if (!warnings.Contains(recordWarning))
                warnings.Add(recordWarning);
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

    private decimal ResolveConservativePercent(AthleteAdaptationContextDto context)
    {
        if (context.ConservativeEstimatedRecordPercent is > 0m and <= 100m)
            return context.ConservativeEstimatedRecordPercent.Value;

        return (adaptationOptions ?? new AdaptationOptions()).GetEffectiveEstimatedRecordConservativePercent();
    }

    private static IReadOnlyList<string> BuildEstimatedAndMissingRecordWarnings(
        IReadOnlyList<AthleteRecordRemoteDto>? currentRecords,
        IReadOnlyList<SuggestedExerciseDto>? baseExercises,
        IReadOnlyList<SuggestedExerciseDto>? adaptedExercises,
        decimal conservativePercent)
    {
        if (currentRecords is not { Count: > 0 })
            return [];

        var warnings = new List<string>();
        var alertedEstimatedExerciseIds = new HashSet<Guid>();
        var alertedEstimatedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var candidateExercises = (baseExercises ?? [])
            .Concat(adaptedExercises ?? [])
            .ToList();

        foreach (var exercise in candidateExercises)
        {
            var matched = FindMatchingRecord(currentRecords, exercise);
            if (matched is null || !IsEstimatedOrigin(matched.Origin))
                continue;

            var alreadyAlerted =
                (matched.ExerciseId != Guid.Empty && !alertedEstimatedExerciseIds.Add(matched.ExerciseId)) ||
                (!string.IsNullOrWhiteSpace(matched.ExerciseName) && !alertedEstimatedNames.Add(matched.ExerciseName.Trim()));

            if (alreadyAlerted)
                continue;

            warnings.Add(
                $"Alerta de dato estimado: el 1RM de {matched.ExerciseName} ({matched.WeightOneRepMaxKg:0.##} kg) es estimado; la carga se calcula con escalón conservador ({conservativePercent:0.#}%).");
        }

        return warnings;
    }

    private static AthleteRecordRemoteDto? FindMatchingRecord(
        IReadOnlyList<AthleteRecordRemoteDto> records,
        SuggestedExerciseDto exercise)
    {
        if (exercise.ExerciseId is { } exerciseId && exerciseId != Guid.Empty)
        {
            var byId = records.FirstOrDefault(r => r.ExerciseId == exerciseId);
            if (byId is not null)
                return byId;
        }

        if (!string.IsNullOrWhiteSpace(exercise.ExerciseName))
        {
            var targetName = exercise.ExerciseName.Trim();
            return records.FirstOrDefault(r =>
                !string.IsNullOrWhiteSpace(r.ExerciseName) &&
                string.Equals(r.ExerciseName.Trim(), targetName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static bool IsEstimatedOrigin(string? origin) =>
        !string.IsNullOrWhiteSpace(origin) &&
        (origin.Contains("Estimated", StringComparison.OrdinalIgnoreCase) ||
         origin.Contains("Estimad", StringComparison.OrdinalIgnoreCase));

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
