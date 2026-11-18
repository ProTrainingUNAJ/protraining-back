using Application.Common.DayAdjustments;
using Application.DTOs.DayAdjustments;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.DayConflicts.GetDayConflicts;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.DayAdjustments.CreateDayAdjustment;

public sealed record CreateDayAdjustmentCommand(Guid AthleteId);

/// <summary>
/// Genera la propuesta de ajuste del día a partir de los conflictos de dolor de hoy (#157 · KAN-452).
/// El ejercicio sustituto se elige por reglas (el primero de la lista de KAN-387) y la IA solo redacta la justificación.
/// Si ya existe una propuesta Pending para hoy, devuelve esa sin duplicarla.
/// </summary>
public sealed class CreateDayAdjustmentCommandHandler(
    IDayAdjustmentRepository dayAdjustmentRepository,
    GetDayConflictsQueryHandler getDayConflictsHandler,
    IAiService aiService,
    IUnitOfWork unitOfWork,
    IExerciseLibraryMcpClient? exerciseLibraryMcpClient = null)
{
    public async Task<DayAdjustmentResponseDto> HandleAsync(
        CreateDayAdjustmentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.AthleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es obligatorio.");

        var today = ArgentinaTime.Today;

        var existingPending = await dayAdjustmentRepository.GetPendingByAthleteAndDateAsync(
            command.AthleteId,
            today,
            cancellationToken);

        if (existingPending is not null)
        {
            return DayAdjustmentMapper.ToResponseDto(existingPending);
        }

        var conflictsResponse = await getDayConflictsHandler.HandleAsync(
            new GetDayConflictsQuery(command.AthleteId),
            cancellationToken);

        if (!conflictsResponse.HasConflicts || conflictsResponse.Conflicts.Count == 0)
        {
            throw new DomainException("El atleta no tiene conflictos de dolor en la rutina de hoy.");
        }

        if (!conflictsResponse.SessionId.HasValue || conflictsResponse.SessionId.Value == Guid.Empty)
        {
            throw new DomainException("El atleta no tiene una sesión programada para el día de hoy.");
        }

        var items = new List<DayAdjustmentItemDto>(conflictsResponse.Conflicts.Count);

        foreach (var conflict in conflictsResponse.Conflicts)
        {
            var availableSubstitutes = conflict.Substitutes;
            if ((availableSubstitutes is null || availableSubstitutes.Count == 0)
                && exerciseLibraryMcpClient is not null
                && !string.IsNullOrWhiteSpace(conflict.Zone))
            {
                var mcpResult = await exerciseLibraryMcpClient.GetSubstitutesAsync(
                    conflict.Zone,
                    cancellationToken);

                availableSubstitutes = mcpResult.Substitutes
                    .Select(s => new Application.DTOs.DayConflicts.DayConflictSubstituteDto(s.ExerciseId, s.Name))
                    .ToList();
            }

            if (availableSubstitutes is null || availableSubstitutes.Count == 0)
            {
                throw new DomainException(
                    $"No hay ejercicios sustitutos habilitados para '{conflict.Name}' en la zona '{conflict.Zone}'.");
            }

            // Regla determinística: elegir siempre el primer sustituto de la lista habilitada (KAN-387)
            var firstSubstitute = availableSubstitutes[0];

            items.Add(new DayAdjustmentItemDto(
                OriginalExerciseId: conflict.ExerciseId,
                OriginalExerciseName: conflict.Name,
                SuggestedSubstituteExerciseId: firstSubstitute.ExerciseId,
                SuggestedSubstituteExerciseName: firstSubstitute.Name,
                SubstituteExerciseId: firstSubstitute.ExerciseId,
                SubstituteExerciseName: firstSubstitute.Name,
                Zone: conflict.Zone,
                PainLevel: conflict.PainLevel,
                Reason: conflict.Reason,
                AvailableSubstitutes: availableSubstitutes));
        }

        var (justification, provider) = await aiService.GenerateDayAdjustmentJustificationAsync(
            items,
            cancellationToken);

        var itemsJson = DayAdjustmentMapper.SerializeItems(items);

        var entity = DayAdjustment.Create(
            athleteId: command.AthleteId,
            date: today,
            sessionId: conflictsResponse.SessionId.Value,
            itemsJson: itemsJson,
            justification: justification,
            provider: provider);

        await dayAdjustmentRepository.AddAsync(entity, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DayAdjustmentMapper.ToResponseDto(entity);
    }
}
