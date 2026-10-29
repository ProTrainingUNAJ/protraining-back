using Application.Interfaces.Persistence;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data;

public class RefreshTokenRepository(AppDbContext context) : IRefreshTokenRepository
{
    public async Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        return await context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == token, cancellationToken);
    }

    public async Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default)
    {
        await context.RefreshTokens.AddAsync(refreshToken, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllActiveByDeviceAsync(Guid userId, string deviceId, CancellationToken cancellationToken = default)
    {
        var activeTokens = await context.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.DeviceId == deviceId && !rt.IsRevoked)
            .ToListAsync(cancellationToken);

        var utcNow = DateTime.UtcNow;
        foreach (var token in activeTokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = utcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }


}
