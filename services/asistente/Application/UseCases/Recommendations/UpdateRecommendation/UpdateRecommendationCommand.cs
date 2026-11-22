using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Domain.Exceptions;

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

        var text = command.RecommendationText;
        if (recommendation.Type == RecommendationType.Habits)
        {
            // Lo que edita el PF en una de alimentación pasa por las mismas reglas que lo que propone la IA:
            // sin suplementos ni pautas para bajar de peso, y siempre con el aviso del nutricionista (KAN-634).
            if (RecommendationOutputValidator.TryFindForbiddenSupplement(text, out var supplement))
                throw new DomainException($"La recomendación de alimentación no puede mencionar suplementos ('{supplement}').");
            if (RecommendationOutputValidator.TryFindForbiddenWeightLoss(text, out var weightLoss))
                throw new DomainException($"La recomendación de alimentación no puede incluir pautas para bajar de peso ('{weightLoss}').");
            text = RecommendationMapper.EnsureDisclaimer(text);
        }

        recommendation.UpdateContent(command.Title, text);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return RecommendationMapper.ToResponseDto(recommendation);
    }
}
