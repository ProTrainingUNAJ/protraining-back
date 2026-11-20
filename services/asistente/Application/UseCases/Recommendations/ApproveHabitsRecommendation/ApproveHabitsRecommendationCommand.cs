using Application.DTOs.Recommendations;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;

namespace Application.UseCases.Recommendations.ApproveHabitsRecommendation;

public sealed record ApproveHabitsRecommendationCommand(Guid RecommendationId);

/// <summary>
/// Aprueba/publica una recomendación de hábitos para que sea visible al atleta (contrato hábitos).
/// Reutiliza la entidad y flujo de aprobación de recomendaciones (KAN-492) con validación de tipo.
/// </summary>
public sealed class ApproveHabitsRecommendationCommandHandler(
    IAthleteRecommendationRepository repository,
    IUnitOfWork unitOfWork)
{
    public const string ApprovedMessage = "Recomendación de hábitos aprobada y visible para el atleta.";

    public async Task<ApproveHabitsRecommendationResponseDto> HandleAsync(
        ApproveHabitsRecommendationCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.RecommendationId == Guid.Empty)
            throw new NotFoundException("La recomendación de hábitos solicitada no fue encontrada.");

        var recommendation = await repository.GetByIdForUpdateAsync(command.RecommendationId, cancellationToken)
            ?? throw new NotFoundException("La recomendación de hábitos solicitada no fue encontrada.");

        if (recommendation.Type != RecommendationType.Habits)
            throw new NotFoundException("La recomendación de hábitos solicitada no fue encontrada.");

        if (recommendation.Status != RecommendationStatus.Pending)
            throw new ConflictException(recommendation.GetNonPendingStatusMessage());

        recommendation.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ApproveHabitsRecommendationResponseDto(ApprovedMessage);
    }
}
