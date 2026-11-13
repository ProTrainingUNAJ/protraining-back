using Application.Common.PlanDrafts;
using Application.DTOs.PlanDrafts;
using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.PlanDrafts.GetPlanDraft;

public sealed record GetPlanDraftQuery(Guid Id);

public sealed class GetPlanDraftQueryHandler(IPlanDraftRepository planDraftRepository)
{
    public async Task<PlanDraftResponseDto> HandleAsync(
        GetPlanDraftQuery query,
        CancellationToken cancellationToken = default)
    {
        var draft = await planDraftRepository.GetByIdAsNoTrackingAsync(query.Id, cancellationToken)
            ?? throw new NotFoundException("El borrador solicitado no existe.");

        return PlanDraftMapper.ToResponseDto(draft);
    }
}
