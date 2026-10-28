using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IExerciseRestrictionRepository
{
    Task<List<ExerciseRestriction>> GetRestrictionsByZonesAndExercisesAsync(
        IEnumerable<string> zones,
        IEnumerable<Guid> exerciseIds,
        CancellationToken cancellationToken = default);

    Task<ExerciseRestriction?> GetRestrictionByExerciseAndZoneAsync(
        Guid exerciseId,
        string zone,
        CancellationToken cancellationToken = default);
}

