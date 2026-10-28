using Domain.Entities;

namespace Application.Interfaces.Persistence;

/// <summary>
/// contrato de persistencia para tokens de refresco
/// </summary>
public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task RevokeAllActiveByDeviceAsync(Guid userId, string deviceId, CancellationToken cancellationToken = default);

}
