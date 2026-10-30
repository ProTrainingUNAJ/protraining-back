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

    public static CategoryInvitation Create(Guid categoryId, DateTime expiresAt, string? customToken = null)
    {
        if (categoryId == Guid.Empty)
            throw new DomainException("El identificador de la categoría es inválido.");

        if (expiresAt <= DateTime.UtcNow)
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

    public bool IsExpired(DateTime utcNow) => utcNow >= ExpiresAt;
}