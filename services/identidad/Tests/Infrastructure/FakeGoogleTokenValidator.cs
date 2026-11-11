using Application.Interfaces.Services;

namespace Identidad.Tests.Infrastructure;

/// <summary>
/// Implementación de prueba para validar tokens simulados de Google sin conexión a servicios externos.
/// </summary>
public sealed class FakeGoogleTokenValidator : IGoogleTokenValidator
{
    private readonly Dictionary<string, GoogleTokenPayload> _validTokens = new();

    public void RegisterToken(string token, GoogleTokenPayload payload)
    {
        _validTokens[token] = payload;
    }

    public Task<GoogleTokenPayload> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (_validTokens.TryGetValue(idToken, out var payload))
        {
            return Task.FromResult(payload);
        }

        throw new InvalidOperationException("Invalid or unrecognized fake Google token.");
    }
}