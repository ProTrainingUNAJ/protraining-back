using Application.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data;

public sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        PersistenceErrors.ExecuteAsync(() => context.SaveChangesAsync(cancellationToken));

    public Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsRelational())
        {
            return action();
        }

        return PersistenceErrors.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await action();
            await transaction.CommitAsync(cancellationToken);
            return 0;
        });
    }
}
