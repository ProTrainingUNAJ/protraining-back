using Application.Common.Alerts;
using Application.DTOs.Alerts;
using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Alerts.GetAlertById;

public sealed record GetAlertByIdQuery(Guid Id);

public sealed class GetAlertByIdQueryHandler(IPreventiveAlertRepository repository)
{
    public async Task<PreventiveAlertResponseDto> HandleAsync(
        GetAlertByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var alert = await repository.GetByIdAsNoTrackingAsync(query.Id, cancellationToken)
            ?? throw new NotFoundException("El aviso solicitado no fue encontrado.");

        return AlertMapper.ToResponseDto(alert);
    }
}

