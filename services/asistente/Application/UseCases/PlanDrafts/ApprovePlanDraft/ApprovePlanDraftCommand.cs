using Application.Common.PlanDrafts;
using Application.DTOs.PlanDrafts;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.PlanDrafts.ApprovePlanDraft;

public sealed record ApprovePlanDraftCommand(Guid Id);

public sealed class ApprovePlanDraftCommandHandler(
    IPlanDraftRepository planDraftRepository,
    ITrainingPlanServiceClient trainingPlanServiceClient,
    IUnitOfWork unitOfWork)
{
    public const string CreatePlanFailedMessage = "No se pudo crear la planificación, probá de nuevo";
    public const string AssignmentFailedWarning = "Se creó la planificación pero no se pudo asignar";

    public async Task<ApprovePlanDraftResponseDto> HandleAsync(
        ApprovePlanDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        var draft = await planDraftRepository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("El borrador solicitado no existe.");

        if (draft.Status == PlanDraftStatus.Discarded)
            throw new ConflictException(draft.GetNonEditableStatusMessage());

        // Idempotencia: aprobar dos veces no crea dos planificaciones; devuelve el approved_plan_id existente
        if (draft.Status == PlanDraftStatus.Approved &&
            draft.ApprovedPlanId.HasValue &&
            draft.ApprovedPlanId.Value != Guid.Empty)
        {
            var existingPlanId = draft.ApprovedPlanId.Value;
            return new ApprovePlanDraftResponseDto(
                Id: draft.Id,
                PlanId: existingPlanId,
                ApprovedPlanId: existingPlanId,
                Status: draft.Status.ToString(),
                Assigned: true,
                AssignmentId: null,
                Warning: null,
                Warnings: []
            );
        }

        var content = await ResolveReferenceExercisesAsync(draft, cancellationToken);
        var savePlanRequest = PlanDraftMapper.ToSaveTrainingPlanRequest(content, draft.Goal);

        Guid createdPlanId;
        try
        {
            createdPlanId = await trainingPlanServiceClient.CreatePlanAsync(savePlanRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TrainingServiceUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TrainingServiceUnavailableException(CreatePlanFailedMessage, ex);
        }

        if (createdPlanId == Guid.Empty)
            throw new TrainingServiceUnavailableException(CreatePlanFailedMessage);

        var assignRequest = PlanDraftMapper.ToAssignTrainingPlanRequest(draft, createdPlanId);
        AssignTrainingPlanResult assignResult;
        try
        {
            assignResult = await trainingPlanServiceClient.TryAssignPlanAsync(assignRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            assignResult = new AssignTrainingPlanResult(Assigned: false, AssignmentId: null);
        }

        draft.Approve(createdPlanId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var warning = assignResult.Assigned ? null : AssignmentFailedWarning;
        IReadOnlyList<string> warnings = assignResult.Assigned ? [] : [AssignmentFailedWarning];

        return new ApprovePlanDraftResponseDto(
            Id: draft.Id,
            PlanId: createdPlanId,
            ApprovedPlanId: createdPlanId,
            Status: draft.Status.ToString(),
            Assigned: assignResult.Assigned,
            AssignmentId: assignResult.AssignmentId,
            Warning: warning,
            Warnings: warnings
        ); 
    }
    private async Task<PlanDraftContentDto> ResolveReferenceExercisesAsync(
        PlanDraft draft,
        CancellationToken cancellationToken)
    {
        var content = PlanDraftMapper.DeserializeContent(draft.Content);
        var createdByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var anyResolved = false;

        foreach (var week in content.Weeks ?? [])
        {
            foreach (var session in week.Sessions ?? [])
            {
                for (var i = 0; i < session.Exercises.Count; i++)
                {
                    var exercise = session.Exercises[i];
                    var needsCreation = exercise.IsReference || !exercise.ExerciseId.HasValue || exercise.ExerciseId.Value == Guid.Empty;
                    if (!needsCreation)
                        continue;

                    if (string.IsNullOrWhiteSpace(exercise.ExerciseName))
                        throw new DomainException("Un ejercicio de referencia no tiene nombre para poder crearlo en la biblioteca.");

                    var trimmedName = exercise.ExerciseName.Trim();
                    if (!createdByName.TryGetValue(trimmedName, out var resolvedId))
                    {
                        var muscleGroup = string.IsNullOrWhiteSpace(exercise.MuscleGroup)
                            ? "General"
                            : exercise.MuscleGroup.Trim();

                        resolvedId = await trainingPlanServiceClient.EnsureExerciseCreatedAsync(
                            new CreateExerciseRemoteRequestDto(Name: trimmedName, MuscleGroup: muscleGroup),
                            cancellationToken);

                        createdByName[trimmedName] = resolvedId;
                    }

                    session.Exercises[i] = exercise with
                    {
                        ExerciseId = resolvedId,
                        IsReference = false
                    };
                    anyResolved = true;
                }
            }
        }

        if (anyResolved)
        {
            draft.UpdateContent(PlanDraftMapper.SerializeContent(content));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return content;
    }
}
