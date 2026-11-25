using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.DTOs.Auth;
using Application.DTOs.Users;
using Identidad.Tests.Infrastructure;
using Infrastructure.Data;
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
        _client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.0.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}");
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

    [Fact]
    public async Task RegisterPf_WhenRateLimitExceeded_Returns429TooManyRequests()
    {
        // Arrange: Cliente con IP específica vía X-Forwarded-For para aislar el contador de rate limiting (#KAN-601)
        var rateLimitClient = _factory.CreateClient();
        rateLimitClient.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.195");

        // Act & Assert: Las primeras 5 peticiones son procesadas
        for (var i = 1; i <= 5; i++)
        {
            var request = new RegisterPfRequestDto(
                FullName: $"PF RateLimit {i}",
                Email: $"ratelimit.{i}@protraining.com",
                Password: "Password123"
            );
            var response = await rateLimitClient.PostAsJsonAsync("/api/v1/auth/register-pf", request);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        // La sexta petición supera la cuota configurada (5 peticiones cada 15 min) -> 429 Too Many Requests
        var excessRequest = new RegisterPfRequestDto(
            FullName: "PF RateLimit Exceso",
            Email: "ratelimit.exceso@protraining.com",
            Password: "Password123"
        );
        var rateLimitedResponse = await rateLimitClient.PostAsJsonAsync("/api/v1/auth/register-pf", excessRequest);
        Assert.Equal(HttpStatusCode.TooManyRequests, rateLimitedResponse.StatusCode);
    }

    [Fact]
    public async Task ResendConfirmation_WhenRateLimitExceeded_Returns429TooManyRequests()
    {
        // Arrange: Cliente con IP dedicada
        var rateLimitClient = _factory.CreateClient();
        rateLimitClient.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.196");

        // Act & Assert: Primeras 5 peticiones permitidas
        for (var i = 1; i <= 5; i++)
        {
            var res = await rateLimitClient.PostAsJsonAsync("/api/v1/auth/resend-confirmation", new ResendConfirmationRequestDto($"user{i}@test.com"));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, res.StatusCode);
        }

        // Sexta petición bloqueada con 429
        var excessRes = await rateLimitClient.PostAsJsonAsync("/api/v1/auth/resend-confirmation", new ResendConfirmationRequestDto("user6@test.com"));
        Assert.Equal(HttpStatusCode.TooManyRequests, excessRes.StatusCode);
    }

    [Fact]
    public async Task Login_SeedPfAndSeedAthlete_LoginSuccessfully_WithoutEmailConfirmationRestriction()
    {
        // Arrange: Asegurar que el seed de desarrollo está ejecutado (#KAN-601)
        await IdentityDataSeed.SeedAsync(_factory.Services);

        // Act: Login del PF inicial del seed (debe entrar sin requerir confirmación KAN-601)
        var pfLoginRequest = new LoginRequestDto("pf@protraining.com", "ProTraining2026!", "test-device");
        var pfLoginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", pfLoginRequest);

        // Assert PF
        Assert.Equal(HttpStatusCode.OK, pfLoginResponse.StatusCode);
        var pfLoginData = await pfLoginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(pfLoginData);
        Assert.NotNull(pfLoginData.AccessToken);
        Assert.Equal("PreparadorFisico", pfLoginData.User.Role);

        // Arrange & Act: Login del Atleta inicial del seed
        var athleteLoginRequest = new LoginRequestDto("atleta@protraining.com", "ProTraining2026!", "test-device");
        var athleteLoginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", athleteLoginRequest);

        // Assert Atleta
        Assert.Equal(HttpStatusCode.OK, athleteLoginResponse.StatusCode);
        var athleteLoginData = await athleteLoginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(athleteLoginData);
        Assert.NotNull(athleteLoginData.AccessToken);
        Assert.Equal("Atleta", athleteLoginData.User.Role);
    }

    [Fact]
    public async Task Isolation_SecondRegisteredPf_HasIndependentIdentityAndCannotAccessOtherPfData()
    {
        // Arrange: Registrar y confirmar PF 1
        var email1 = "pf1.aislado@protraining.com";
        await _client.PostAsJsonAsync("/api/v1/auth/register-pf", new RegisterPfRequestDto("PF Uno", email1, "Password123"));
        var emailPf1 = _factory.EmailSender.SentConfirmationEmails.First(e => e.ToEmail == email1);
        await _client.PostAsJsonAsync("/api/v1/auth/confirm-email", new ConfirmEmailRequestDto(emailPf1.UserId, emailPf1.ConfirmationToken));

        var login1Response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(email1, "Password123", "device-1"));
        var login1Data = await login1Response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(login1Data);

        // Arrange: Registrar y confirmar PF 2
        var email2 = "pf2.aislado@protraining.com";
        await _client.PostAsJsonAsync("/api/v1/auth/register-pf", new RegisterPfRequestDto("PF Dos", email2, "Password123"));
        var emailPf2 = _factory.EmailSender.SentConfirmationEmails.First(e => e.ToEmail == email2);
        await _client.PostAsJsonAsync("/api/v1/auth/confirm-email", new ConfirmEmailRequestDto(emailPf2.UserId, emailPf2.ConfirmationToken));

        var login2Response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(email2, "Password123", "device-2"));
        var login2Data = await login2Response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(login2Data);

        // Assert 1: Identidades independientes
        Assert.NotEqual(login1Data.User.Id, login2Data.User.Id);

        // Act: PF 2 actualiza su propio perfil
        var client2 = _factory.CreateClient();
        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login2Data.AccessToken);
        var updateProfileRes = await client2.PutAsJsonAsync("/api/v1/users/me/profile", new UpdateProfileRequestDto("PF Dos Modificado", "+5491188887777"));
        Assert.Equal(HttpStatusCode.OK, updateProfileRes.StatusCode);

        // Assert 2: Verificar en base de datos que PF 1 permanece inalterado
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user1 = await userManager.FindByEmailAsync(email1);
        var user2 = await userManager.FindByEmailAsync(email2);

        Assert.NotNull(user1);
        Assert.NotNull(user2);
        Assert.Equal("PF Uno", user1.FullName);
        Assert.Equal("PF Dos Modificado", user2.FullName);
    }

    [Fact]
    public async Task DeactivateAccount_RotatesSecurityStamp_AndPreventsFurtherSessionUsage()
    {
        // Arrange: Registrar y confirmar un PF
        var email = "stamp.baja@protraining.com";
        await _client.PostAsJsonAsync("/api/v1/auth/register-pf", new RegisterPfRequestDto("PF Stamp", email, "Password123"));
        var sentEmail = _factory.EmailSender.SentConfirmationEmails.First(e => e.ToEmail == email);
        await _client.PostAsJsonAsync("/api/v1/auth/confirm-email", new ConfirmEmailRequestDto(sentEmail.UserId, sentEmail.ConfirmationToken));

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(email, "Password123", "test-device"));
        var loginData = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(loginData);

        string? initialSecurityStamp;
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            initialSecurityStamp = user.SecurityStamp;
            Assert.False(string.IsNullOrWhiteSpace(initialSecurityStamp));
        }

        var authClient = _factory.CreateClient();
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginData.AccessToken);

        // Act: Desactivar cuenta
        var okResponse = await authClient.PostAsJsonAsync("/api/v1/users/me/deactivate", new DeactivateAccountRequestDto("Password123"));
        Assert.Equal(HttpStatusCode.NoContent, okResponse.StatusCode);

        // Assert: SecurityStamp rotado y cuenta inactiva
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var userAfter = await userManager.FindByEmailAsync(email);
            Assert.NotNull(userAfter);
            Assert.False(userAfter.IsActive);
            Assert.NotEqual(initialSecurityStamp, userAfter.SecurityStamp);
        }

        // Login posterior denegado con 401
        var reLoginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(email, "Password123", "test-device"));
        Assert.Equal(HttpStatusCode.Unauthorized, reLoginResponse.StatusCode);
    }
}
