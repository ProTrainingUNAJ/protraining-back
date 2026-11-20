using Application.DTOs.Tracking;

namespace Application.Interfaces.Services;

public interface ITrackingClient
{
    Task<IReadOnlyList<TrackingBestMarkDto>> GetAthleteBestMarksAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default);
}
