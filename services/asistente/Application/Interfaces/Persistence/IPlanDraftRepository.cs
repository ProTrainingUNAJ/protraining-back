using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IPlanDraftRepository
{
    Task AddAsync(PlanDraft planDraft, CancellationToken cancellationToken = default);
    Task<PlanDraft?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PlanDraft?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default);
}
