using Application.Common.Alerts;
using Application.DTOs.Alerts;
using Application.Interfaces.Persistence;
using Domain.Entities;

namespace Application.UseCases.Alerts.CreatePreventiveAlert;

public sealed record CreatePreventiveAlertCommand(CreatePreventiveAlertRequestDto Dto);

public sealed class CreatePreventiveAlertCommandHandler(
    IPreventiveAlertRepository repository,
    IUnitOfWork unitOfWork)
{
    public async Task<PreventiveAlertResponseDto> HandleAsync(
        CreatePreventiveAlertCommand command,
        CancellationToken cancellationToken = default)
    {
        var dto = command.Dto;

        var severity = AlertSeverity.Medium;
        if (!string.IsNullOrWhiteSpace(dto.Severity) &&
            Enum.TryParse<AlertSeverity>(dto.Severity, true, out var parsedSeverity))
        {
            severity = parsedSeverity;
        }

        var type = AlertType.AccumulatedFatigue;
        if (!string.IsNullOrWhiteSpace(dto.Type) &&
            Enum.TryParse<AlertType>(dto.Type, true, out var parsedType))
        {
            type = parsedType;
        }

        SuggestedAdjustmentType? adjustmentType = null;
        if (!string.IsNullOrWhiteSpace(dto.SuggestedAdjustmentType) &&
            Enum.TryParse<SuggestedAdjustmentType>(dto.SuggestedAdjustmentType, true, out var parsedAdj))
        {
            adjustmentType = parsedAdj;
        }

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

        await repository.AddAsync(alert, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AlertMapper.ToResponseDto(alert);
    }
}

