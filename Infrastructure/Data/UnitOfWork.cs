using Application.Interfaces.Persistence;

namespace Infrastructure.Data;

public sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        PersistenceErrors.ExecuteAsync(() => context.SaveChangesAsync(cancellationToken));
}
