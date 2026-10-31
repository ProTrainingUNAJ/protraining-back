using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IRecordProposalRepository
{
    Task<RecordProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<RecordProposal>> ListAsync(RecordProposalStatus? status, CancellationToken cancellationToken = default);
    Task<RecordProposal?> GetPendingAsync(Guid athleteId, Guid exerciseId, CancellationToken cancellationToken = default);
    Task<List<RecordProposal>> ListPendingByAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default);
    Task AddAsync(RecordProposal proposal, CancellationToken cancellationToken = default);
    Task UpdateAsync(RecordProposal proposal, CancellationToken cancellationToken = default);
}
