using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;

namespace Application.UseCases.Recommendations.ApproveRecommendation;

public sealed record ApproveRecommendationCommand(
    Guid Id,
    string? Title = null,
    string? RecommendationText = null);

public sealed class ApproveRecommendationCommandHandler(
    IAthleteRecommendationRepository repository,
    IUnitOfWork unitOfWork)
{
    public async Task<AthleteRecommendationResponseDto> HandleAsync(
        ApproveRecommendationCommand command,
        CancellationToken cancellationToken = default)
    {
        var recommendation = await repository.GetByIdForUpdateAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("La recomendación solicitada no fue encontrada.");

        if (recommendation.Status != RecommendationStatus.Pending)
            throw new ConflictException(recommendation.GetNonPendingStatusMessage());

        if (command.Title is not null || command.RecommendationText is not null)
        {
            recommendation.UpdateContent(
                command.Title ?? recommendation.Title,
                command.RecommendationText ?? recommendation.RecommendationText);
        }

        recommendation.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return RecommendationMapper.ToResponseDto(recommendation);
    }
}
