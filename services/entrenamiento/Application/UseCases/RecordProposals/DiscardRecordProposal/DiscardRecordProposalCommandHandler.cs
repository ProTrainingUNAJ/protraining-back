using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Common;
using Domain.Entities;

namespace Application.UseCases.RecordProposals.DiscardRecordProposal;

public class DiscardRecordProposalCommandHandler
{
    private readonly IRecordProposalRepository _recordProposalRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DiscardRecordProposalCommandHandler(
        IRecordProposalRepository recordProposalRepository,
        IUnitOfWork unitOfWork)
    {
        _recordProposalRepository = recordProposalRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(
        DiscardRecordProposalCommand command,
        CancellationToken cancellationToken = default)
    {
        var proposal = await _recordProposalRepository.GetByIdAsync(command.Id, cancellationToken);
        if (proposal == null)
        {
            throw new NotFoundException($"No se encontró la propuesta de récord con ID '{command.Id}'.");
        }

        if (proposal.Status != RecordProposalStatus.Pending)
        {
            throw new ConflictException("La propuesta no está pendiente.");
        }

        proposal.Discard();
        await _recordProposalRepository.UpdateAsync(proposal, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

