using System.Security.Cryptography;
using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class CategoryInvitation : BaseEntity
{
    public Guid CategoryId { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }

    private CategoryInvitation() { }

    public static CategoryInvitation Create(
        Guid categoryId,
        DateTime expiresAt,
        string? customToken = null,
        DateTime? utcNow = null)
    {
        if (categoryId == Guid.Empty)
            throw new DomainException("El identificador de la categoría es inválido.");

        var now = utcNow ?? DateTime.UtcNow;
        if (expiresAt <= now)
            throw new DomainException("La fecha de expiración debe ser en el futuro.");

        var token = string.IsNullOrWhiteSpace(customToken)
            ? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()
            : customToken.Trim();

        return new CategoryInvitation
        {
            CategoryId = categoryId,
            Token = token,
            ExpiresAt = expiresAt
        };
    }

    public static CategoryInvitation Create(
        Guid categoryId,
        DateTime expiresAt,
        TimeProvider timeProvider,
        string? customToken = null)
        => Create(categoryId, expiresAt, customToken, timeProvider.GetUtcNow().UtcDateTime);

    public bool IsExpired(DateTime utcNow) => utcNow >= ExpiresAt;
}