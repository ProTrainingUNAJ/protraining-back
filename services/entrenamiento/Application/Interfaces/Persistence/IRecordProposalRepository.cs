using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IRecordProposalRepository
{
    Task<RecordProposal?> GetPendingAsync(Guid athleteId, Guid exerciseId, CancellationToken cancellationToken = default);
    Task<List<RecordProposal>> ListPendingByAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default);
    Task AddAsync(RecordProposal proposal, CancellationToken cancellationToken = default);
    Task UpdateAsync(RecordProposal proposal, CancellationToken cancellationToken = default);
}

