using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IAthleteCategoryRepository
{
    Task<IReadOnlyList<AthleteCategory>> ListByAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Athlete>> ListAthletesByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
    // Sin el filtro por PF del token: solo para endpoints internos con rol 'Servicio' (KAN-578)
    Task<IReadOnlyList<Athlete>> ListAthletesByCategoryInternalAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> ListCategoriesByAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default);
    Task<AthleteCategory?> GetAsync(Guid athleteId, Guid categoryId, CancellationToken cancellationToken = default);
    Task AddAsync(AthleteCategory athleteCategory, CancellationToken cancellationToken = default);
    Task DeleteAsync(AthleteCategory athleteCategory, CancellationToken cancellationToken = default);
    Task<bool> HasAthletesByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
}

