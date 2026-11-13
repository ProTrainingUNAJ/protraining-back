using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Application.DTOs.Auth;
using Domain.Entities;
using Identidad.Tests.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Identidad.Tests.Integration;

[Trait("Category", "Security")]
public sealed class InternalAuthSecurityTests : IAsyncDisposable
{
    private readonly IdentidadTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public InternalAuthSecurityTests()
    {
        _factory = new IdentidadTestApplicationFactory();
        _client = _factory.CreateClient();
    }

    private string CreateServiceToken()
    {
        var key = "clave_local_de_desarrollo_cambiar_32+";
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "gateway"),
            new(JwtRegisteredClaimNames.Name, "Gateway"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("role", "Servicio")
        };

        var token = new JwtSecurityToken(
            issuer: "protraining-identidad",
            audience: "protraining",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task ValidateSession_WithoutToken_Returns401Unauthorized()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/internal/auth/validate-session",
            new ValidateSessionRequestDto(Guid.NewGuid(), "some-stamp"));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidateSession_WithNonServiceRole_Returns403Forbidden()
    {
        // Arrange: Token con rol Atleta en lugar de Servicio
        var athlete = await _factory.SeedUserAsync("atleta.internal@protraining.com", "Atleta Internal", "Atleta");
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequestDto(athlete.Email!, "PasswordSegura123!", "device-1"));
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(loginResult);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/internal/auth/validate-session",
            new ValidateSessionRequestDto(athlete.Id, athlete.SecurityStamp!));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ValidateSession_WithValidServiceToken_AndMatchingStamp_Returns200_IsValidTrue()
    {
        // Arrange
        var athlete = await _factory.SeedUserAsync("atleta.valid@protraining.com", "Atleta Valid", "Atleta");
        var serviceToken = CreateServiceToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/internal/auth/validate-session",
            new ValidateSessionRequestDto(athlete.Id, athlete.SecurityStamp!));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ValidateSessionResponseDto>();
        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.False(result.MustChangePassword);
    }

    [Fact]
    public async Task ValidateSession_WithMismatchingStamp_Returns200_IsValidFalse()
    {
        // Arrange
        var athlete = await _factory.SeedUserAsync("atleta.mismatch@protraining.com", "Atleta Mismatch", "Atleta");
        var serviceToken = CreateServiceToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);

        // Act: Enviar un sello de seguridad anterior/distinto
        var response = await _client.PostAsJsonAsync("/api/v1/internal/auth/validate-session",
            new ValidateSessionRequestDto(athlete.Id, "stamp-obsoleto-123"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ValidateSessionResponseDto>();
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Equal("SecurityStampMismatch", result.FailureReason);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        _client.Dispose();
    }
}
