using Application.DTOs.Alerts;
using Domain.Entities;

namespace Application.Common.Alerts;

public static class AlertMapper
{
    public static PreventiveAlertResponseDto ToResponseDto(PreventiveAlert alert)
    {
        return new PreventiveAlertResponseDto(
            Id: alert.Id,
            AthleteId: alert.AthleteId,
            AthleteName: alert.AthleteName,
            GeneratedAt: alert.GeneratedAtUtc,
            Severity: alert.Severity.ToString(),
            Reason: alert.Reason,
            Suggestion: alert.Suggestion,
            Reviewed: alert.Reviewed,
            SuggestedAdjustmentType: alert.SuggestedAdjustmentType?.ToString(),
            SuggestedAdjustmentPercentage: alert.SuggestedAdjustmentPercentage,
            Status: alert.Status.ToString());
    }
}

