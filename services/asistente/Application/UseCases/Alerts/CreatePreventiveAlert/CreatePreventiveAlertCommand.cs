using Application.Common.Alerts;
using Application.DTOs.Alerts;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.Alerts.CreatePreventiveAlert;

public sealed record CreatePreventiveAlertCommand(CreatePreventiveAlertRequestDto Dto);

public sealed class CreatePreventiveAlertCommandHandler(
    IPreventiveAlertRepository repository,
    IStructureAthleteClient structureAthleteClient,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public async Task<PreventiveAlertResponseDto> HandleAsync(
        CreatePreventiveAlertCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.PfId.HasValue || currentUser.PfId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Usuario no autenticado o sin un preparador físico asignado.");
        }

        var dto = command.Dto;

        var severity = ParseOptionalEnum<AlertSeverity>(dto.Severity, "severidad") ?? AlertSeverity.Medium;
        var type = ParseOptionalEnum<AlertType>(dto.Type, "tipo de aviso") ?? AlertType.AccumulatedFatigue;
        var adjustmentType = ParseOptionalEnum<SuggestedAdjustmentType>(dto.SuggestedAdjustmentType, "tipo de ajuste sugerido");

        var alert = PreventiveAlert.Create(
            athleteId: dto.AthleteId,
            athleteName: dto.AthleteName,
            reason: dto.Reason,
            suggestion: dto.Suggestion,
            severity: severity,
            type: type,
            suggestedAdjustmentType: adjustmentType,
            suggestedAdjustmentPercentage: dto.SuggestedAdjustmentPercentage,
            suggestedAdjustmentPayload: dto.SuggestedAdjustmentPayload,
            categoryId: dto.CategoryId,
            generatedAtUtc: DateTime.UtcNow);

        var isOwnAthlete = await structureAthleteClient.IsOwnAthleteAsync(dto.AthleteId, cancellationToken);
        if (!isOwnAthlete)
        {
            throw new NotFoundException("El atleta no existe o no pertenece al preparador físico autenticado.");
        }

        await repository.AddAsync(alert, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AlertMapper.ToResponseDto(alert);
    }

    private static TEnum? ParseOptionalEnum<TEnum>(string? rawValue, string fieldLabel)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (!Enum.TryParse<TEnum>(rawValue.Trim(), true, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw new DomainException($"El valor '{rawValue}' no es válido para {fieldLabel}.");
        }

        return parsed;
    }
}

