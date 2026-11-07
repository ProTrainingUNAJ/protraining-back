using System.Text.Json;
using Application.DTOs.TrainingSessions;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.TrainingSessions.ApplyAthleteAdjustment;

public sealed class ApplyAthleteSessionAdjustmentCommandHandler(
    ITrainingSessionRepository trainingSessionRepository,
    IAthleteSessionAdjustmentRepository adjustmentRepository,
    IExerciseRepository exerciseRepository,
    IStructureClient structureClient,
    IUnitOfWork unitOfWork)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<AthleteSessionAdjustmentResponseDto> Handle(
        ApplyAthleteSessionAdjustmentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.SessionId == Guid.Empty)
            throw new NotFoundException("La sesión de entrenamiento no existe.");

        var request = command.Request ?? throw new DomainException("La solicitud de ajuste es obligatoria.");

        if (request.AthleteId == Guid.Empty)
            throw new NotFoundException("El atleta no existe o no es tuyo.");

        if (request.Date == default)
            throw new DomainException("La fecha del ajuste es obligatoria.");

        if (request.Items is null || request.Items.Count == 0)
            throw new DomainException("Debe incluir al menos un cambio de ejercicio para la sesión.");

        foreach (var item in request.Items)
        {
            if (item.OriginalExerciseId == Guid.Empty || item.SubstituteExerciseId == Guid.Empty)
                throw new DomainException("Los identificadores del ejercicio original y del sustituto son obligatorios.");
        }

        // 1. Verificar que la sesión exista y pertenezca al PF del token.
        var session = await trainingSessionRepository.GetByIdAsync(command.SessionId, cancellationToken)
            ?? throw new NotFoundException("La sesión de entrenamiento no existe.");

        // 2. Verificar que el atleta exista y pertenezca al PF del token.
        var athlete = await structureClient.GetAthleteByIdAsync(request.AthleteId, cancellationToken)
            ?? throw new NotFoundException("El atleta no existe o no es tuyo.");

        // 3. Verificar que los ejercicios sustitutos existan en el catálogo.
        var substituteIds = request.Items
            .Select(i => i.SubstituteExerciseId)
            .Distinct()
            .ToList();

        var foundExercises = await exerciseRepository.ListByIdsAsync(substituteIds, cancellationToken);
        if (foundExercises.Count != substituteIds.Count)
            throw new DomainException("Uno o más ejercicios sustitutos no existen en el catálogo.");

        // 4. Resolver y vincular al plan vigente (#168 · KAN-463).
        var planIdFromSession = await trainingSessionRepository.GetPlanIdBySessionIdAsync(session.Id, cancellationToken);
        var sessionPlanId = planIdFromSession ?? session.Week?.TrainingPlanId ?? Guid.Empty;
        var trainingPlanId = request.TrainingPlanId ?? (sessionPlanId != Guid.Empty ? sessionPlanId : Guid.Empty);

        if (request.TrainingPlanId.HasValue &&
            request.TrainingPlanId.Value != Guid.Empty &&
            sessionPlanId != Guid.Empty &&
            request.TrainingPlanId.Value != sessionPlanId)
        {
            throw new DomainException("La sesión indicada no pertenece al plan de entrenamiento especificado.");
        }

        var itemsJson = JsonSerializer.Serialize(request.Items, JsonOptions);

        var existing = await adjustmentRepository.GetBySessionAthleteAndDateAsync(
            session.Id,
            athlete.Id,
            request.Date,
            cancellationToken);

        AthleteSessionAdjustment adjustment;
        if (existing is not null)
        {
            existing.Update(itemsJson, request.Note, trainingPlanId);
            adjustment = existing;
        }
        else
        {
            adjustment = AthleteSessionAdjustment.Create(
                trainingSessionId: session.Id,
                athleteId: athlete.Id,
                date: request.Date,
                itemsJson: itemsJson,
                note: request.Note,
                trainingPlanId: trainingPlanId);

            await adjustmentRepository.AddAsync(adjustment, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AthleteSessionAdjustmentResponseDto(
            Id: adjustment.Id,
            SessionId: adjustment.TrainingSessionId,
            AthleteId: adjustment.AthleteId,
            Date: adjustment.Date,
            Note: adjustment.Note,
            Items: request.Items,
            TrainingPlanId: adjustment.TrainingPlanId != Guid.Empty ? adjustment.TrainingPlanId : null);
    }
}
