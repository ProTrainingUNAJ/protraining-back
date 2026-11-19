using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;

namespace Application.UseCases.Recommendations.DiscardRecommendation;

public sealed record DiscardRecommendationCommand(Guid Id);

public sealed class DiscardRecommendationCommandHandler(
    IAthleteRecommendationRepository repository,
    IUnitOfWork unitOfWork)
{
    public async Task<AthleteRecommendationResponseDto> HandleAsync(
        DiscardRecommendationCommand command,
        CancellationToken cancellationToken = default)
    {
        var recommendation = await repository.GetByIdForUpdateAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("La recomendación solicitada no fue encontrada.");

        if (recommendation.Status != RecommendationStatus.Pending)
            throw new ConflictException(recommendation.GetNonPendingStatusMessage());

        recommendation.Discard();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return RecommendationMapper.ToResponseDto(recommendation);
    }
}
