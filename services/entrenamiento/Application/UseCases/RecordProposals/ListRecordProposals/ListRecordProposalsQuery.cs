using Domain.Entities;

namespace Application.UseCases.RecordProposals.ListRecordProposals;

public record ListRecordProposalsQuery(RecordProposalStatus? Status = RecordProposalStatus.Pending);

