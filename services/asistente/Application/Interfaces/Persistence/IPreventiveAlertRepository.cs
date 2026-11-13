using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IPreventiveAlertRepository
{
    Task AddAsync(PreventiveAlert alert, CancellationToken cancellationToken = default);
    Task<PreventiveAlert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PreventiveAlert?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PreventiveAlert>> ListAsync(bool onlyUnreviewed = true, AlertSeverity? severity = null, CancellationToken cancellationToken = default);
}

