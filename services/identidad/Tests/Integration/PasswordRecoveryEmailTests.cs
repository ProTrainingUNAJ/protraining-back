using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Application.DTOs.Auth;
using Domain.Entities;
using Identidad.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Identidad.Tests.Integration;

public class PasswordRecoveryEmailTests : IAsyncLifetime
{
    private readonly IdentidadTestApplicationFactory _factory = new();
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        _factory.EmailSender.Clear();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task<User> SeedUserAsync(string email, string password, string role = "Atleta", bool mustChangePassword = false)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            FullName = "Usuario Prueba",
            IsActive = true,
            EmailConfirmed = true,
            MustChangePassword = mustChangePassword,
            SecurityStamp = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, role);
        return user;
    }

    [Fact]
    public async Task FP01_Given_RegisteredUser_When_ForgotPasswordAndResetWithToken_Then_CanLoginWithNewPassword_OldPasswordFails_AndOldSessionsRevoked()
    {
        // 1. Arrange: Usuario activo con contraseña inicial
        const string email = "atleta.fp01@protraining.app";
        const string oldPassword = "PasswordInicial1";
        const string newPassword = "PasswordNueva123";
        await SeedUserAsync(email, oldPassword);

        // 2. Act 1: Solicitar recuperación de contraseña (POST /forgot-password)
        var forgotResponse = await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequestDto(email));
        Assert.Equal(HttpStatusCode.Accepted, forgotResponse.StatusCode);

        var forgotResult = await forgotResponse.Content.ReadFromJsonAsync<ForgotPasswordResponseDto>();
        Assert.NotNull(forgotResult);
        Assert.Equal("Si el correo está registrado, recibirás un enlace para recuperar tu contraseña.", forgotResult.Message);

        // 3. Assert 1: Se capturó el correo con el enlace
        Assert.Single(_factory.EmailSender.SentEmails);
        var sentEmail = _factory.EmailSender.SentEmails[0];
        Assert.Equal(email, sentEmail.ToEmail);
        Assert.Contains("/reset-password?", sentEmail.ResetLink);

        // Extraer token del query string del enlace
        var uri = new Uri(sentEmail.ResetLink);
        var queryParams = HttpUtility.ParseQueryString(uri.Query);
        var tokenFromLink = queryParams["token"];
        Assert.False(string.IsNullOrWhiteSpace(tokenFromLink));

        // 4. Act 2: Confirmar nueva contraseña (POST /reset-password)
        var resetResponse = await _client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordWithTokenRequestDto(
            Email: email,
            Token: tokenFromLink!,
            NewPassword: newPassword
        ));
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        var resetResult = await resetResponse.Content.ReadFromJsonAsync<ResetPasswordWithTokenResponseDto>();
        Assert.NotNull(resetResult);
        Assert.Equal("Contraseña actualizada exitosamente.", resetResult.Message);

        // 5. Assert 2: Iniciar sesión con la nueva contraseña tiene éxito
        var loginNewResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(email, newPassword, "device-fp01"));
        Assert.Equal(HttpStatusCode.OK, loginNewResponse.StatusCode);

        // 6. Assert 3: Iniciar sesión con la contraseña anterior es rechazado (401)
        var loginOldResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(email, oldPassword, "device-fp01"));
        Assert.Equal(HttpStatusCode.Unauthorized, loginOldResponse.StatusCode);
    }

    [Fact]
    public async Task FP02_Given_UnregisteredEmail_When_ForgotPassword_Then_Returns202Accepted_AndNoEmailSent()
    {
        // Arrange: Correo que no existe en el sistema
        const string unknownEmail = "inexistente.fp02@protraining.app";

        // Act: Solicitar recuperación
        var response = await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequestDto(unknownEmail));

        // Assert: Devuelve exactamente HTTP 202 Accepted sin revelar la inexistencia de la cuenta y sin enviar correo
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ForgotPasswordResponseDto>();
        Assert.NotNull(result);
        Assert.Equal("Si el correo está registrado, recibirás un enlace para recuperar tu contraseña.", result.Message);

        Assert.Empty(_factory.EmailSender.SentEmails);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalido")]
    [InlineData("sin-arroba.com")]
    public async Task FP02b_Given_InvalidEmailFormat_When_ForgotPassword_Then_Returns400BadRequest(string invalidEmail)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequestDto(invalidEmail));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FP03_Given_InvalidOrExpiredOrTamperedToken_When_ResetPassword_Then_Returns400BadRequest_AndSpanishErrorMessage()
    {
        const string email = "atleta.fp03@protraining.app";
        await SeedUserAsync(email, "PasswordValida1");

        // Act: Intentar reseteo con token alterado / espurio
        var response = await _client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordWithTokenRequestDto(
            Email: email,
            Token: "token_invalido_adulterado_123",
            NewPassword: "NuevaPasswordValida1"
        ));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("El enlace de recuperación es inválido o ha vencido. Solicitá uno nuevo.", content);
    }

    [Theory]
    [InlineData("corta1")]       // Menos de 8 caracteres
    [InlineData("sololetras")]   // 8+ caracteres pero sin dígitos
    public async Task FP04_Given_PasswordNotMeetingUnifiedPolicy_When_ResetPassword_Then_Returns400_WithValidationErrors(string invalidPassword)
    {
        const string email = "atleta.fp04@protraining.app";
        await SeedUserAsync(email, "PasswordValida1");

        // Solicitar token válido
        await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequestDto(email));
        Assert.Single(_factory.EmailSender.SentEmails);
        var uri = new Uri(_factory.EmailSender.SentEmails[0].ResetLink);
        var token = HttpUtility.ParseQueryString(uri.Query)["token"]!;

        // Act: Intentar resetear con contraseña que viola la política unificada
        var response = await _client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordWithTokenRequestDto(
            Email: email,
            Token: token,
            NewPassword: invalidPassword
        ));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("No se pudo restablecer la contraseña", content);
    }

    [Fact]
    public async Task FP05_Given_TokenBelongingToDifferentUser_When_ResetPassword_Then_Returns400BadRequest()
    {
        const string userA = "usera.fp05@protraining.app";
        const string userB = "userb.fp05@protraining.app";
        await SeedUserAsync(userA, "PasswordValida1");
        await SeedUserAsync(userB, "PasswordValida2");

        // Generar token para User A
        await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequestDto(userA));
        var uri = new Uri(_factory.EmailSender.SentEmails[0].ResetLink);
        var tokenUserA = HttpUtility.ParseQueryString(uri.Query)["token"]!;

        // Act: Intentar usar el token de User A para cambiar la clave de User B
        var response = await _client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordWithTokenRequestDto(
            Email: userB,
            Token: tokenUserA,
            NewPassword: "NuevaPasswordValida1"
        ));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("El enlace de recuperación es inválido o ha vencido. Solicitá uno nuevo.", content);
    }

    [Fact]
    public async Task FP06_Given_UsedToken_When_ResetPasswordTwice_Then_SecondAttemptReturns400()
    {
        const string email = "atleta.fp06@protraining.app";
        await SeedUserAsync(email, "PasswordValida1");

        await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequestDto(email));
        var uri = new Uri(_factory.EmailSender.SentEmails[0].ResetLink);
        var token = HttpUtility.ParseQueryString(uri.Query)["token"]!;

        // Primer reseteo exitoso
        var firstResponse = await _client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordWithTokenRequestDto(
            Email: email,
            Token: token,
            NewPassword: "NuevaPasswordValida1"
        ));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Segundo reseteo con el mismo token: debe fallar
        var secondResponse = await _client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordWithTokenRequestDto(
            Email: email,
            Token: token,
            NewPassword: "OtraPasswordValida2"
        ));
        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);
        var content = await secondResponse.Content.ReadAsStringAsync();
        Assert.Contains("El enlace de recuperación es inválido o ha vencido. Solicitá uno nuevo.", content);
    }

    [Fact]
    public async Task FP07_Given_SmtpFailure_When_ForgotPassword_Then_StillReturns202Accepted_WithoutLeakingException()
    {
        const string email = "atleta.fp07@protraining.app";
        await SeedUserAsync(email, "PasswordValida1");

        // Simular que el servidor SMTP falla
        _factory.EmailSender.ShouldFail = true;

        var response = await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequestDto(email));

        // Debe seguir respondiendo 202 sin exponer el error interno de SMTP
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ForgotPasswordResponseDto>();
        Assert.NotNull(result);
        Assert.Equal("Si el correo está registrado, recibirás un enlace para recuperar tu contraseña.", result.Message);
    }

    [Fact]
    public async Task FP08_Given_UserWithMustChangePassword_When_ResetsPasswordViaEmail_Then_MustChangePasswordIsCleared()
    {
        const string email = "atleta.fp08@protraining.app";
        await SeedUserAsync(email, "PasswordTemporal1", mustChangePassword: true);

        // Solicitar token y resetear
        await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequestDto(email));
        var uri = new Uri(_factory.EmailSender.SentEmails[0].ResetLink);
        var token = HttpUtility.ParseQueryString(uri.Query)["token"]!;

        var resetResponse = await _client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordWithTokenRequestDto(
            Email: email,
            Token: token,
            NewPassword: "NuevaPasswordDefinitiva1"
        ));
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        // Comprobar en base de datos que MustChangePassword ahora es false
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.False(user.MustChangePassword);
    }
}
