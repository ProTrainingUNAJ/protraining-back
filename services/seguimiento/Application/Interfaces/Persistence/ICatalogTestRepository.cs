using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface ICatalogTestRepository
{
    // null = sin filtro, devuelve el catálogo completo. Solo retorna tests activos
    // (los desactivados no se exponen) y nunca borrados. El filtro global del AppDbContext
    // ya restringe a los tests del PF actual o del catálogo global.
    Task<List<CatalogTest>> ListAsync(MotorCapacity? motorCapacity, CancellationToken cancellationToken = default);

    // Test por id para el caso de uso de confirmación (KAN-426): va tracked (sin
    // AsNoTracking) porque la misma unidad de trabajo cachea el exerciseId resuelto.
    // null = inexistente o de otro PF (filtro global) → 404 en el caso de uso.
    Task<CatalogTest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
