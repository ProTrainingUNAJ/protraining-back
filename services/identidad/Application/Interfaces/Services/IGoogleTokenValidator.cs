namespace Application.Interfaces.Services;

/// <summary>
/// Contiene la información validada extraída del ID Token de Google.
/// </summary>
public record GoogleTokenPayload(
    string Subject,
    string Email,
    bool EmailVerified,
    string? Name = null
);

/// <summary>
/// Servicio para validar criptográficamente tokens de identidad de Google.
/// </summary>
public interface IGoogleTokenValidator
{
    Task<GoogleTokenPayload> ValidateAsync(string idToken, CancellationToken cancellationToken = default);
}
