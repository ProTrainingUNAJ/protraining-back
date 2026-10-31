using Application.DTOs.RecordProposals;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Application.UseCases.RecordProposals.ListRecordProposals;

public class ListRecordProposalsQueryHandler
{
    private readonly IRecordProposalRepository _recordProposalRepository;
    private readonly IStructureClient _structureClient;

    public ListRecordProposalsQueryHandler(
        IRecordProposalRepository recordProposalRepository,
        IStructureClient structureClient)
    {
        _recordProposalRepository = recordProposalRepository;
        _structureClient = structureClient;
    }

    public async Task<List<RecordProposalDto>> Handle(
        ListRecordProposalsQuery query,
        CancellationToken cancellationToken = default)
    {
        var proposals = await _recordProposalRepository.ListAsync(query.Status, cancellationToken);

        var athleteNames = new Dictionary<Guid, string>();
        var result = new List<RecordProposalDto>(proposals.Count);

        foreach (var proposal in proposals)
        {
            if (!athleteNames.TryGetValue(proposal.AthleteId, out var athleteName))
            {
                var athlete = await _structureClient.GetAthleteByIdAsync(proposal.AthleteId, cancellationToken);
                athleteName = athlete?.FullName ?? string.Empty;
                athleteNames[proposal.AthleteId] = athleteName;
            }

            result.Add(new RecordProposalDto(
                proposal.Id,
                proposal.AthleteId,
                athleteName,
                proposal.ExerciseId,
                proposal.Exercise?.Name ?? string.Empty,
                proposal.CurrentWeight,
                proposal.ProposedWeight,
                proposal.PerformedWeight,
                proposal.PerformedReps,
                proposal.Status.ToString(),
                proposal.CreatedAt
            ));
        }

        return result;
    }
}

