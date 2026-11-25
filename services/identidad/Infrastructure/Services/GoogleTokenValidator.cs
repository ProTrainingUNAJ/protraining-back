using Application.Interfaces.Services;
using Google.Apis.Auth;
using Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>
/// Valida criptográficamente el ID Token de Google contra los certificados oficiales de Google.
/// Aplica principio fail-closed: rechaza cualquier token si Google:ClientId no está configurado.
/// </summary>
public sealed class GoogleTokenValidator(
    IOptions<GoogleAuthOptions> options,
    ILogger<GoogleTokenValidator> logger) : IGoogleTokenValidator
{
    public async Task<GoogleTokenPayload> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            throw new ArgumentException("El token de Google es obligatorio.", nameof(idToken));
        }

        var clientId = options.Value.ClientId?.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            logger.LogError("Google:ClientId no está configurado. Se rechaza la autenticación con Google (fail-closed).");
            throw new InvalidOperationException("La autenticación con Google no está configurada.");
        }

        var settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = new[] { clientId }
        };

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);

            if (payload.Issuer is not ("accounts.google.com" or "https://accounts.google.com"))
            {
                throw new InvalidJwtException($"Issuer de Google inválido: {payload.Issuer}");
            }

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
