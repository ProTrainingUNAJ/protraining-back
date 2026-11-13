using Application.DTOs.AthleteRecords;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.UseCases.AthleteRecord;
using Domain.Common;
using Domain.Entities;

namespace Application.UseCases.RecordProposals.ApproveRecordProposal;

public class ApproveRecordProposalCommandHandler
{
    private readonly IRecordProposalRepository _recordProposalRepository;
    private readonly IAthleteRecordRepository _athleteRecordRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ApproveRecordProposalCommandHandler(
        IRecordProposalRepository recordProposalRepository,
        IAthleteRecordRepository athleteRecordRepository,
        IUnitOfWork unitOfWork)
    {
        _recordProposalRepository = recordProposalRepository;
        _athleteRecordRepository = athleteRecordRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<AthleteRecordDto> Handle(
        ApproveRecordProposalCommand command,
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

        var activeRecord = await _athleteRecordRepository.GetActiveRecordAsync(proposal.AthleteId, proposal.ExerciseId, cancellationToken);
        if (activeRecord == null || activeRecord.Weight != proposal.CurrentWeight)
        {
            throw new ConflictException("El récord cambió; revisá la propuesta");
        }

        var exerciseName = proposal.Exercise?.Name
            ?? await _athleteRecordRepository.GetExerciseNameAsync(proposal.ExerciseId, cancellationToken)
            ?? string.Empty;

        var newRecord = Domain.Entities.AthleteRecord.Create(
            proposal.AthleteId,
            proposal.ExerciseId,
            proposal.ProposedWeight,
            RecordSource.Epley,
            DateTime.UtcNow
        );

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            activeRecord.Deactivate();
            await _athleteRecordRepository.UpdateAsync(activeRecord, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _athleteRecordRepository.AddAsync(newRecord, cancellationToken);
            proposal.Approve();
            await _recordProposalRepository.UpdateAsync(proposal, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return AthleteRecordMapper.ToDto(newRecord, exerciseName);
    }
}
