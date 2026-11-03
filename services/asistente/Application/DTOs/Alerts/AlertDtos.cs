using Domain.Entities;

namespace Application.DTOs.Alerts;

public sealed record PreventiveAlertResponseDto(
    Guid Id,
    Guid AthleteId,
    string AthleteName,
    DateTime GeneratedAt,
    string Severity,
    string Reason,
    string Suggestion,
    bool Reviewed,
    string? SuggestedAdjustmentType = null,
    int? SuggestedAdjustmentPercentage = null,
    string? Status = null);

public sealed record CreatePreventiveAlertRequestDto(
    Guid AthleteId,
    string AthleteName,
    string Reason,
    string Suggestion,
    string? Severity = null,
    string? Type = null,
    string? SuggestedAdjustmentType = null,
    int? SuggestedAdjustmentPercentage = null,
    string? SuggestedAdjustmentPayload = null,
    Guid? CategoryId = null);

public sealed record ReviewAlertResponseDto(
    string Message,
    Guid AlertId);

