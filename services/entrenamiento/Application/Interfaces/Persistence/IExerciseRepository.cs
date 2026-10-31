using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IExerciseRepository
{
    Task AddExercise(Exercise exercise, CancellationToken cancellationToken = default);
    Task<(List<Exercise> Items, int TotalCount)> ListAsync(string? searchTerm, string? equipment, string? muscleGroup, bool usedByPf, int page, int pageSize, CancellationToken cancellationToken = default);

    // Variantes (KAN-554): solo los ejercicios base, paginados. Un base entra si él o alguna de sus
    // variantes cumple los filtros (por ejemplo, la búsqueda coincide con "Agarre cerrado").
    Task<(List<Exercise> Items, int TotalCount)> ListBasesAsync(string? searchTerm, string? equipment, string? muscleGroup, bool usedByPf, int page, int pageSize, CancellationToken cancellationToken = default);

    // Ejercicios por id (KAN-414): el Asistente valida que los ejercicios de un borrador existan.
    Task<List<Exercise>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    // Todas las variantes de los ejercicios base indicados.
    Task<List<Exercise>> GetVariantsAsync(IReadOnlyCollection<Guid> baseExerciseIds, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Exercise?> FindByNameAsync(string name, CancellationToken cancellationToken = default);
}
