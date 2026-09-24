using Application.Interfaces.Services;
using Infrastructure.Data;

namespace Infrastructure.Services;

public sealed class DatabaseHealthService(AppDbContext context) : IDatabaseHealthService
{
    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.Database.CanConnectAsync(cancellationToken);
        }
        catch
        {
            return false;
        }
    }
}
