using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.DTOs.Auth;
using Application.DTOs.Users;
using Identidad.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Domain.Entities;
using Xunit;

namespace Identidad.Tests.Integration;

public class RegisterPfIntegrationTests : IClassFixture<IdentidadTestApplicationFactory>
{
    private readonly IdentidadTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public RegisterPfIntegrationTests(IdentidadTestApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.EmailSender.Clear();
    }

    [Fact]
    public async Task RegisterPf_WithValidData_Returns201AndSendsConfirmationEmail()
    {
        // Arrange
        var request = new RegisterPfRequestDto(
            FullName: "Entrenador Nuevo",
            Email: "nuevo.pf@protraining.com",
            Password: "Password123",
            Phone: "+5491122334455"
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register-pf", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RegisterPfResponseDto>();
        Assert.NotNull(result);
        Assert.Equal("nuevo.pf@protraining.com", result.Email);
        Assert.Equal("Entrenador Nuevo", result.FullName);
        Assert.Contains("nuevo.pf@protraining.com", result.Message);

        // Verificar que el correo de confirmación se envió
        var sentEmails = _factory.EmailSender.SentConfirmationEmails;
        Assert.Contains(sentEmails, e => e.ToEmail == "nuevo.pf@protraining.com");
    }

    [Fact]
    public async Task RegisterPf_WhenEmailAlreadyExists_Returns409Conflict()
    {
        // Arrange: Registrar un PF inicial
        var email = "existente@protraining.com";
        await _client.PostAsJsonAsync("/api/v1/auth/register-pf", new RegisterPfRequestDto(
            FullName: "PF Original",
            Email: email,
            Password: "Password123"
        ));

        var duplicateRequest = new RegisterPfRequestDto(
            FullName: "PF Duplicado",
            Email: email,
            Password: "Password123"
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register-pf", duplicateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Login_WhenPfEmailNotConfirmed_Returns403Forbidden()
    {
        // Arrange: Registrar un PF nuevo sin confirmar
        var email = "sinconfirmar@protraining.com";
        var regRequest = new RegisterPfRequestDto(
            FullName: "PF Sin Confirmar",
            Email: email,
            Password: "Password123"
        );
        var regResponse = await _client.PostAsJsonAsync("/api/v1/auth/register-pf", regRequest);
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);

        // Act: Intentar login
        var loginRequest = new LoginRequestDto(email, "Password123", "test-device");
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, loginResponse.StatusCode);
    }

    [Fact]
    public async Task ConfirmEmail_WithValidToken_EnablesLogin()
    {
        // Arrange: Registrar un PF nuevo
        var email = "confirmar.exito@protraining.com";
        var regRequest = new RegisterPfRequestDto(
            FullName: "PF A Confirmar",
            Email: email,
            Password: "Password123"
        );
        await _client.PostAsJsonAsync("/api/v1/auth/register-pf", regRequest);

        var sentEmail = _factory.EmailSender.SentConfirmationEmails.First(e => e.ToEmail == email);

        // Act: Confirmar email
        var confirmRequest = new ConfirmEmailRequestDto(sentEmail.UserId, sentEmail.ConfirmationToken);
        var confirmResponse = await _client.PostAsJsonAsync("/api/v1/auth/confirm-email", confirmRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        // Act 2: Login ahora debe ser exitoso
        var loginRequest = new LoginRequestDto(email, "Password123", "test-device");
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task ResendConfirmation_SendsNewConfirmationEmail()
    {
        // Arrange
        var email = "reenvio@protraining.com";
        var regRequest = new RegisterPfRequestDto(
            FullName: "PF Reenvio",
            Email: email,
            Password: "Password123"
        );
        await _client.PostAsJsonAsync("/api/v1/auth/register-pf", regRequest);
        _factory.EmailSender.Clear();

        // Act
        var resendResponse = await _client.PostAsJsonAsync("/api/v1/auth/resend-confirmation", new ResendConfirmationRequestDto(email));

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, resendResponse.StatusCode);
        Assert.Contains(_factory.EmailSender.SentConfirmationEmails, e => e.ToEmail == email);
    }

    [Fact]
    public async Task UpdateProfile_UpdatesFullNameAndPhone()
    {
        // Arrange: Crear y confirmar un PF nuevo
        var email = "pf.perfil@protraining.com";
        await _client.PostAsJsonAsync("/api/v1/auth/register-pf", new RegisterPfRequestDto("PF Inicial", email, "Password123"));
        var sentEmail = _factory.EmailSender.SentConfirmationEmails.First(e => e.ToEmail == email);
        await _client.PostAsJsonAsync("/api/v1/auth/confirm-email", new ConfirmEmailRequestDto(sentEmail.UserId, sentEmail.ConfirmationToken));

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(email, "Password123", "test-device"));
        var loginData = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(loginData);

        var updateClient = _factory.CreateClient();
        updateClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginData.AccessToken);

        // Act
        var profileRequest = new UpdateProfileRequestDto(FullName: "Profesor Físico Actualizado", Phone: "+5491199887766");
        var response = await updateClient.PutAsJsonAsync("/api/v1/users/me/profile", profileRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<ProfileResponseDto>();
        Assert.NotNull(profile);
        Assert.Equal("Profesor Físico Actualizado", profile.FullName);
        Assert.Equal("5491199887766", profile.PhoneNumber);
    }

    [Fact]
    public async Task DeactivateAccount_DisablesAccountAndPreventsLogin()
    {
        // Arrange: Crear un PF nuevo y confirmarlo
        var email = "parabaja@protraining.com";
        var regRequest = new RegisterPfRequestDto("PF Para Baja", email, "Password123");
        await _client.PostAsJsonAsync("/api/v1/auth/register-pf", regRequest);
        var sentEmail = _factory.EmailSender.SentConfirmationEmails.First(e => e.ToEmail == email);
        await _client.PostAsJsonAsync("/api/v1/auth/confirm-email", new ConfirmEmailRequestDto(sentEmail.UserId, sentEmail.ConfirmationToken));

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(email, "Password123", "test-device"));
        var loginData = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(loginData);

        var authClient = _factory.CreateClient();
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginData.AccessToken);

        // Act 1: Intentar desactivar con contraseña errónea
        var badResponse = await authClient.PostAsJsonAsync("/api/v1/users/me/deactivate", new DeactivateAccountRequestDto("WrongPass123"));
        Assert.Equal(HttpStatusCode.Unauthorized, badResponse.StatusCode);

        // Act 2: Desactivar con contraseña correcta
        var okResponse = await authClient.PostAsJsonAsync("/api/v1/users/me/deactivate", new DeactivateAccountRequestDto("Password123"));
        Assert.Equal(HttpStatusCode.NoContent, okResponse.StatusCode);

        // Act 3: Intentar loguear de nuevo debe fallar 401
        var reLoginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(email, "Password123", "test-device"));
        Assert.Equal(HttpStatusCode.Unauthorized, reLoginResponse.StatusCode);
    }
}
