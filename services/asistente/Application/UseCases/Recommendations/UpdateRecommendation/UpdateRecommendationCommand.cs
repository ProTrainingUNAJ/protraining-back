using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;

namespace Application.UseCases.Recommendations.UpdateRecommendation;

public sealed record UpdateRecommendationCommand(
    Guid Id,
    string Title,
    string RecommendationText);

public sealed class UpdateRecommendationCommandHandler(
    IAthleteRecommendationRepository repository,
    IUnitOfWork unitOfWork)
{
    public async Task<AthleteRecommendationResponseDto> HandleAsync(
        UpdateRecommendationCommand command,
        CancellationToken cancellationToken = default)
    {
        var recommendation = await repository.GetByIdForUpdateAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("La recomendación solicitada no fue encontrada.");

        if (recommendation.Status != RecommendationStatus.Pending)
            throw new ConflictException(recommendation.GetNonPendingStatusMessage());

        recommendation.UpdateContent(command.Title, command.RecommendationText);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return RecommendationMapper.ToResponseDto(recommendation);
    }
}
