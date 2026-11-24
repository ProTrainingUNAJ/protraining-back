using Application.DTOs.Athletes;
using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IAthleteRepository
{
    Task<IReadOnlyList<Athlete>> ListAsync(CancellationToken cancellationToken = default);
    Task<Athlete?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Athlete?> GetByIdWithMedicalRecordAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Athlete?> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Indica si ya hay un atleta del PF actual con ese correo (sin distinguir mayúsculas ni espacios).
    /// <paramref name="excludeAthleteId"/> deja afuera al propio atleta al editar su correo.
    /// </summary>
    Task<bool> EmailExistsAsync(string email, Guid? excludeAthleteId = null, CancellationToken cancellationToken = default);
    Task AddAsync(Athlete athlete, CancellationToken cancellationToken = default);
    Task DeleteAsync(Athlete athlete, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetDistinctPfIdsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InternalAthleteSummaryDto>> GetActiveAthletesInternalAsync(CancellationToken cancellationToken = default);
}
