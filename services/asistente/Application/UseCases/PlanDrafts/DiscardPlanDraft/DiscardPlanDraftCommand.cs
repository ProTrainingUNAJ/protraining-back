using Application.Common.PlanDrafts;
using Application.DTOs.PlanDrafts;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;

namespace Application.UseCases.PlanDrafts.DiscardPlanDraft;

public sealed record DiscardPlanDraftCommand(Guid Id);

public sealed class DiscardPlanDraftCommandHandler(
    IPlanDraftRepository planDraftRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<PlanDraftResponseDto> HandleAsync(
        DiscardPlanDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        var draft = await planDraftRepository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("El borrador solicitado no existe.");

        if (draft.Status != PlanDraftStatus.Draft)
            throw new ConflictException(draft.GetNonEditableStatusMessage());

        draft.Discard();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PlanDraftMapper.ToResponseDto(draft);
    }
}
