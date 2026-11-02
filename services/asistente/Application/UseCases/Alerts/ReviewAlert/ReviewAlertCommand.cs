using Application.DTOs.Alerts;
using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Alerts.ReviewAlert;

public sealed record ReviewAlertCommand(Guid AlertId);

public sealed class ReviewAlertCommandHandler(
    IPreventiveAlertRepository repository,
    IUnitOfWork unitOfWork)
{
    public async Task<ReviewAlertResponseDto> HandleAsync(
        ReviewAlertCommand command,
        CancellationToken cancellationToken = default)
    {
        var alert = await repository.GetByIdAsync(command.AlertId, cancellationToken)
            ?? throw new NotFoundException("El aviso solicitado no fue encontrado.");

        alert.MarkAsReviewed(DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ReviewAlertResponseDto(
            Message: "Alerta marcada como revisada exitosamente.",
            AlertId: alert.Id);
    }
}

