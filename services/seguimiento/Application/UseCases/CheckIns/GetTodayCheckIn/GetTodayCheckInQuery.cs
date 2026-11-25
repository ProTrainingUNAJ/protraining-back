using Application.DTOs.CheckIns;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;
using Domain.Common;
using Domain.Exceptions;

namespace Application.UseCases.CheckIns.GetTodayCheckIn;

public sealed record GetTodayCheckInQuery(Guid AthleteId);

// Dolor y bienestar de hoy (KAN-451): devuelve el check-in del día en hora de Argentina
// con sus zonas de dolor (painZones), o 404 si el atleta todavía no lo registró hoy
// (o si pertenece a otro PF, cubierto por el filtro global de PfId en AppDbContext).
public sealed class GetTodayCheckInQueryHandler(
    ICheckInRepository repository,
    ICheckInInjuryPainRepository injuryPainRepository)
{
    public async Task<CheckInDto> HandleAsync(
        GetTodayCheckInQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.AthleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es inválido.");

        var today = ArgentinaTime.Today.ToDateTime(TimeOnly.MinValue);
        var checkIn = await repository.GetNoTrackingByAthleteAndDateAsync(query.AthleteId, today, cancellationToken)
            ?? throw new CheckInNotFoundException();

        var painsByCheckIn = await injuryPainRepository.ListByCheckInIdsAsync(
            new[] { checkIn.Id }, cancellationToken);

        var pains = painsByCheckIn.TryGetValue(checkIn.Id, out var p)
            ? p
            : (IReadOnlyList<CheckInInjuryPainDto>)Array.Empty<CheckInInjuryPainDto>();

        return CheckInMappings.ToDto(checkIn, pains);
    }
}
