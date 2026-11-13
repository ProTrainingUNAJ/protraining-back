using Application.Common.Alerts;
using Application.DTOs.Alerts;
using Application.Interfaces.Persistence;
using Domain.Entities;

namespace Application.UseCases.Alerts.GetAlerts;

public sealed record GetAlertsQuery(bool OnlyUnreviewed = true, string? Severity = null);

public sealed class GetAlertsQueryHandler(IPreventiveAlertRepository repository)
{
    public async Task<IReadOnlyList<PreventiveAlertResponseDto>> HandleAsync(
        GetAlertsQuery query,
        CancellationToken cancellationToken = default)
    {
        AlertSeverity? parsedSeverity = null;
        if (!string.IsNullOrWhiteSpace(query.Severity) &&
            Enum.TryParse<AlertSeverity>(query.Severity, true, out var sev))
        {
            parsedSeverity = sev;
        }

        var alerts = await repository.ListAsync(query.OnlyUnreviewed, parsedSeverity, cancellationToken);
        return alerts.Select(AlertMapper.ToResponseDto).ToList();
    }
}

