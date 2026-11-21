using Domain.Entities;

namespace Application.Interfaces.Persistence
{
    public interface IAthleteVariantRepository
    {
        Task<IReadOnlyList<AthleteVariant>> GetActiveApprovedByAthleteAsync(Guid athleteId, DateOnly date, CancellationToken cancellationToken = default);
        Task<AthleteVariant?> GetActiveByAthleteAndOriginalExerciseAsync(Guid athleteId, Guid originalExerciseId, DateOnly date, CancellationToken cancellationToken = default);
        Task AddAsync(AthleteVariant variant, CancellationToken cancellationToken = default);
    }
}
