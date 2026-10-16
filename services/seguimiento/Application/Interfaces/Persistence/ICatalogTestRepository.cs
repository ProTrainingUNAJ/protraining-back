using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface ICatalogTestRepository
{
    // null = sin filtro, devuelve el catálogo completo. Solo retorna tests activos
    // (los desactivados no se exponen) y nunca borrados. El filtro global del AppDbContext
    // ya restringe a los tests del PF actual o del catálogo global.
    Task<List<CatalogTest>> ListAsync(MotorCapacity? motorCapacity, CancellationToken cancellationToken = default);
}
