using Application.DTOs.Adaptation;

namespace Application.Interfaces.Services;

public interface IAthleteAdaptationContextService
{
    Task<AthleteAdaptationContextDto> GetAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default);
}
