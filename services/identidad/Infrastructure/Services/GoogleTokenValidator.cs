using Application.Interfaces.Services;
using Google.Apis.Auth;
using Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>
/// Valida criptográficamente el ID Token de Google contra los certificados oficiales de Google.
/// </summary>
public sealed class GoogleTokenValidator(
    IOptions<GoogleAuthOptions> options,
    ILogger<GoogleTokenValidator> logger) : IGoogleTokenValidator
{
    public async Task<GoogleTokenPayload> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        var clientId = options.Value.ClientId;

        var settings = new GoogleJsonWebSignature.ValidationSettings();
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            settings.Audience = new[] { clientId };
        }

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            return new GoogleTokenPayload(
                Subject: payload.Subject,
                Email: payload.Email,
                EmailVerified: payload.EmailVerified,
                Name: payload.Name
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fallo al validar el Google ID Token.");
            throw;
        }
    }
}
