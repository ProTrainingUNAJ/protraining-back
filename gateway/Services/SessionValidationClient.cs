using System.Net.Http.Json;

namespace Gateway.Services;

public record ValidateSessionResult(
    bool IsSuccess,
    bool IsValid,
    bool MustChangePassword,
    string? ErrorMessage = null);

public interface ISessionValidationClient
{
    Task<ValidateSessionResult> ValidateSessionAsync(Guid userId, string securityStamp, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cliente HTTP del Gateway hacia el endpoint interno de Identidad para validación en tiempo real (#KAN-539).
/// </summary>
public sealed class SessionValidationClient(
    HttpClient httpClient,
    IServiceTokenFactory tokenFactory,
    ILogger<SessionValidationClient> logger) : ISessionValidationClient
{
    public async Task<ValidateSessionResult> ValidateSessionAsync(Guid userId, string securityStamp, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/internal/auth/validate-session");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenFactory.CreateToken());
            request.Content = JsonContent.Create(new { UserId = userId, SecurityStamp = securityStamp });

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Fallo en validación interna de sesión: Identidad respondió {StatusCode}.", response.StatusCode);
                return new ValidateSessionResult(IsSuccess: false, IsValid: false, MustChangePassword: false, ErrorMessage: $"Identidad HTTP {response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync<ValidateSessionResponse>(cancellationToken: cancellationToken);
            if (result is null)
            {
                return new ValidateSessionResult(IsSuccess: false, IsValid: false, MustChangePassword: false, ErrorMessage: "Respuesta vacía");
            }

            return new ValidateSessionResult(IsSuccess: true, IsValid: result.IsValid, MustChangePassword: result.MustChangePassword);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al comunicarse con Identidad para validar sesión del usuario {UserId}.", userId);
            return new ValidateSessionResult(IsSuccess: false, IsValid: false, MustChangePassword: false, ErrorMessage: ex.Message);
        }
    }

    private record ValidateSessionResponse(bool IsValid, bool MustChangePassword, string? FailureReason);
}
