using Application.DTOs.Training;

namespace Application.Interfaces.Services;

public interface ITrainingClient
{
    Task<TrainingRecordsResultDto> GetAthleteRecordsAsync(
        Guid athleteId,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken cancellationToken = default);
}

