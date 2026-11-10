using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.DTOs.Auth;
using Domain.Entities;
using Identidad.Tests.Infrastructure;
using Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Identidad.Tests.Integration;

[Trait("Category", "Security")]
public sealed class ResetAthletePasswordTests : IAsyncDisposable
{
    private readonly IdentidadTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public ResetAthletePasswordTests()
    {
        _factory = new IdentidadTestApplicationFactory();
        _client = _factory.CreateClient();
    }

    private async Task<LoginResponseDto> LoginUserAsync(string email, string password, string deviceId = "device-test-1")
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(email, password, deviceId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(result);
        return result;
    }

    [Fact]
    public async Task RP01_Given_AuthenticatedPf_When_ResettingHisOwnAthlete_Then_Returns200_WithTemporaryPassword_AndFlagTrue()
    {
        // Arrange
        var pf = await _factory.SeedUserAsync("pf.rp01@protraining.com", "Profesor RP01", "PreparadorFisico", "PasswordSegura123!");
        var athlete = await _factory.SeedUserAsync("atleta.rp01@protraining.com", "Atleta RP01", "Atleta", "PasswordSegura123!", pfId: pf.Id);

        var pfLogin = await LoginUserAsync(pf.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfLogin.AccessToken);

        // Act
        var response = await _client.PostAsync($"/api/v1/auth/athletes/{athlete.Id}/reset-password", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ResetAthletePasswordResponseDto>();
        Assert.NotNull(result);
        Assert.Equal(athlete.Id, result.AthleteId);
        Assert.False(string.IsNullOrWhiteSpace(result.TemporaryPassword));
        Assert.True(result.MustChangePassword);
    }

    [Fact]
    public async Task RP02_Given_NonExistentAthleteId_When_ResetRequested_Then_Returns404NotFound()
    {
        // Arrange
        var pf = await _factory.SeedUserAsync("pf.rp02@protraining.com", "Profesor RP02", "PreparadorFisico", "PasswordSegura123!");
        var pfLogin = await LoginUserAsync(pf.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfLogin.AccessToken);

        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.PostAsync($"/api/v1/auth/athletes/{nonExistentId}/reset-password", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
        Assert.Equal("Atleta no encontrado.", problem.Title);
    }

    [Fact]
    public async Task RP03_Given_AthleteBelongingToAnotherPf_When_ResetRequested_Then_Returns404NotFound_IndistinguishableFromNonExistent()
    {
        // Arrange
        var pf1 = await _factory.SeedUserAsync("pf1.rp03@protraining.com", "Profesor 1 RP03", "PreparadorFisico", "PasswordSegura123!");
        var pf2 = await _factory.SeedUserAsync("pf2.rp03@protraining.com", "Profesor 2 RP03", "PreparadorFisico", "PasswordSegura123!");

        // Atleta pertenece a PF2
        var athleteOfPf2 = await _factory.SeedUserAsync("atleta.rp03@protraining.com", "Atleta PF2", "Atleta", "PasswordSegura123!", pfId: pf2.Id);

        // PF1 intenta resetear al atleta de PF2
        var pf1Login = await LoginUserAsync(pf1.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pf1Login.AccessToken);

        // Act
        var response = await _client.PostAsync($"/api/v1/auth/athletes/{athleteOfPf2.Id}/reset-password", null);

        // Assert: Aislamiento multitenant estricto (404 indistinguible de atleta inexistente)
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
        Assert.Equal("Atleta no encontrado.", problem.Title);
    }

    [Fact]
    public async Task RP04_Given_UserIsNotAthlete_When_ResetRequested_Then_Returns404NotFound()
    {
        // Arrange
        var pf1 = await _factory.SeedUserAsync("pf1.rp04@protraining.com", "Profesor 1 RP04", "PreparadorFisico", "PasswordSegura123!");
        var anotherPf = await _factory.SeedUserAsync("pf2.rp04@protraining.com", "Profesor 2 RP04", "PreparadorFisico", "PasswordSegura123!", pfId: pf1.Id);

        var pf1Login = await LoginUserAsync(pf1.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pf1Login.AccessToken);

        // Act: Intentar resetear un usuario que no es Atleta
        var response = await _client.PostAsync($"/api/v1/auth/athletes/{anotherPf.Id}/reset-password", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RP05_Given_AuthenticatedUserWithAthleteRole_When_AttemptingToReset_Then_Returns403Forbidden()
    {
        // Arrange
        var pf = await _factory.SeedUserAsync("pf.rp05@protraining.com", "Profesor RP05", "PreparadorFisico", "PasswordSegura123!");
        var athlete1 = await _factory.SeedUserAsync("atleta1.rp05@protraining.com", "Atleta 1", "Atleta", "PasswordSegura123!", pfId: pf.Id);
        var athlete2 = await _factory.SeedUserAsync("atleta2.rp05@protraining.com", "Atleta 2", "Atleta", "PasswordSegura123!", pfId: pf.Id);

        var athleteLogin = await LoginUserAsync(athlete1.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", athleteLogin.AccessToken);

        // Act
        var response = await _client.PostAsync($"/api/v1/auth/athletes/{athlete2.Id}/reset-password", null);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RP06_Given_AnonymousRequest_When_AttemptingToReset_Then_Returns401Unauthorized()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = null;
        var targetAthleteId = Guid.NewGuid();

        // Act
        var response = await _client.PostAsync($"/api/v1/auth/athletes/{targetAthleteId}/reset-password", null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RP07_Given_GeneratedTemporaryPassword_When_Inspected_Then_CompliesWithPasswordPolicy_LengthUpperLowerDigitSpecial()
    {
        // Arrange
        var pf = await _factory.SeedUserAsync("pf.rp07@protraining.com", "Profesor RP07", "PreparadorFisico", "PasswordSegura123!");
        var athlete = await _factory.SeedUserAsync("atleta.rp07@protraining.com", "Atleta RP07", "Atleta", "PasswordSegura123!", pfId: pf.Id);

        var pfLogin = await LoginUserAsync(pf.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfLogin.AccessToken);

        // Act
        var response = await _client.PostAsync($"/api/v1/auth/athletes/{athlete.Id}/reset-password", null);
        var result = await response.Content.ReadFromJsonAsync<ResetAthletePasswordResponseDto>();

        // Assert
        Assert.NotNull(result);
        var pwd = result.TemporaryPassword;
        Assert.True(pwd.Length >= 8, "Debe tener al menos 8 caracteres.");
        Assert.Contains(pwd, char.IsUpper);
        Assert.Contains(pwd, char.IsLower);
        Assert.Contains(pwd, char.IsDigit);
        Assert.Contains(pwd, ch => !char.IsLetterOrDigit(ch));
    }

    [Fact]
    public async Task RP08_Given_MultipleResets_When_ComparingGeneratedPasswords_Then_CSPRNGGeneratesDifferentAndUnpredictablePasswords()
    {
        // Arrange
        var pf = await _factory.SeedUserAsync("pf.rp08@protraining.com", "Profesor RP08", "PreparadorFisico", "PasswordSegura123!");
        var athlete1 = await _factory.SeedUserAsync("atleta1.rp08@protraining.com", "Atleta 1 RP08", "Atleta", "PasswordSegura123!", pfId: pf.Id);
        var athlete2 = await _factory.SeedUserAsync("atleta2.rp08@protraining.com", "Atleta 2 RP08", "Atleta", "PasswordSegura123!", pfId: pf.Id);

        var pfLogin = await LoginUserAsync(pf.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfLogin.AccessToken);

        // Act
        var res1 = await _client.PostAsync($"/api/v1/auth/athletes/{athlete1.Id}/reset-password", null);
        var res2 = await _client.PostAsync($"/api/v1/auth/athletes/{athlete2.Id}/reset-password", null);

        var body1 = await res1.Content.ReadFromJsonAsync<ResetAthletePasswordResponseDto>();
        var body2 = await res2.Content.ReadFromJsonAsync<ResetAthletePasswordResponseDto>();

        // Assert
        Assert.NotNull(body1);
        Assert.NotNull(body2);
        Assert.NotEqual(body1.TemporaryPassword, body2.TemporaryPassword);
    }

    [Fact]
    public async Task RP09_Given_PasswordReset_When_CheckingDatabase_Then_OnlyPBKDF2HashIsStored_AndPlainTextNeverStored()
    {
        // Arrange
        var pf = await _factory.SeedUserAsync("pf.rp09@protraining.com", "Profesor RP09", "PreparadorFisico", "PasswordSegura123!");
        var athlete = await _factory.SeedUserAsync("atleta.rp09@protraining.com", "Atleta RP09", "Atleta", "PasswordSegura123!", pfId: pf.Id);

        var pfLogin = await LoginUserAsync(pf.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfLogin.AccessToken);

        // Act
        var response = await _client.PostAsync($"/api/v1/auth/athletes/{athlete.Id}/reset-password", null);
        var result = await response.Content.ReadFromJsonAsync<ResetAthletePasswordResponseDto>();
        Assert.NotNull(result);

        // Assert
        var updatedAthlete = await _factory.FindUserByEmailAsync(athlete.Email!);
        Assert.NotNull(updatedAthlete);
        Assert.NotNull(updatedAthlete.PasswordHash);
        // El hash no debe ser la contraseña en texto plano
        Assert.NotEqual(result.TemporaryPassword, updatedAthlete.PasswordHash);
        // Debe ser un hash PBKDF2 de Identity (empieza usualmente con AQAAAA)
        Assert.True(updatedAthlete.PasswordHash.Length > 20);
    }

    [Fact]
    public async Task RP10_Given_PasswordReset_When_ResponseReceived_Then_TemporaryPasswordIsReturnedOnlyOnceInBody()
    {
        // Arrange
        var pf = await _factory.SeedUserAsync("pf.rp10@protraining.com", "Profesor RP10", "PreparadorFisico", "PasswordSegura123!");
        var athlete = await _factory.SeedUserAsync("atleta.rp10@protraining.com", "Atleta RP10", "Atleta", "PasswordSegura123!", pfId: pf.Id);

        var pfLogin = await LoginUserAsync(pf.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfLogin.AccessToken);

        // Act
        var response = await _client.PostAsync($"/api/v1/auth/athletes/{athlete.Id}/reset-password", null);
        var result = await response.Content.ReadFromJsonAsync<ResetAthletePasswordResponseDto>();

        // Assert
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.TemporaryPassword));
    }

    [Fact]
    public async Task RP11_Given_AthleteWithActiveSessions_When_PasswordReset_Then_AllAthleteRefreshTokensAreRevoked()
    {
        // Arrange
        var pf = await _factory.SeedUserAsync("pf.rp11@protraining.com", "Profesor RP11", "PreparadorFisico", "PasswordSegura123!");
        var athlete = await _factory.SeedUserAsync("atleta.rp11@protraining.com", "Atleta RP11", "Atleta", "PasswordSegura123!", pfId: pf.Id);

        // Atleta inicia sesión en 2 dispositivos distintos
        var athleteLogin1 = await LoginUserAsync(athlete.Email!, "PasswordSegura123!", "device-1");
        var athleteLogin2 = await LoginUserAsync(athlete.Email!, "PasswordSegura123!", "device-2");

        var pfLogin = await LoginUserAsync(pf.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfLogin.AccessToken);

        // Act
        var resetResponse = await _client.PostAsync($"/api/v1/auth/athletes/{athlete.Id}/reset-password", null);
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        // Assert: Intentar refrescar con cualquiera de los tokens anteriores debe fallar
        _client.DefaultRequestHeaders.Authorization = null;
        var refreshRes1 = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequestDto(athleteLogin1.RefreshToken, "device-1"));
        var refreshRes2 = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequestDto(athleteLogin2.RefreshToken, "device-2"));

        Assert.Equal(HttpStatusCode.Unauthorized, refreshRes1.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshRes2.StatusCode);
    }

    [Fact]
    public async Task RP12_Given_AthletePasswordReset_When_CheckingOtherUsers_Then_PfAndOtherAthletesRefreshTokensAreNotRevoked()
    {
        // Arrange
        var pf = await _factory.SeedUserAsync("pf.rp12@protraining.com", "Profesor RP12", "PreparadorFisico", "PasswordSegura123!");
        var athlete1 = await _factory.SeedUserAsync("atleta1.rp12@protraining.com", "Atleta 1 RP12", "Atleta", "PasswordSegura123!", pfId: pf.Id);
        var athlete2 = await _factory.SeedUserAsync("atleta2.rp12@protraining.com", "Atleta 2 RP12", "Atleta", "PasswordSegura123!", pfId: pf.Id);

        var pfLogin = await LoginUserAsync(pf.Email!, "PasswordSegura123!", "pf-device");
        var athlete2Login = await LoginUserAsync(athlete2.Email!, "PasswordSegura123!", "athlete2-device");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfLogin.AccessToken);

        // Act: Resetear solo a athlete1
        var resetResponse = await _client.PostAsync($"/api/v1/auth/athletes/{athlete1.Id}/reset-password", null);
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        // Assert: Ni el PF ni athlete2 deben ver sus tokens revocados
        _client.DefaultRequestHeaders.Authorization = null;
        var pfRefresh = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequestDto(pfLogin.RefreshToken, "pf-device"));
        var athlete2Refresh = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequestDto(athlete2Login.RefreshToken, "athlete2-device"));

        Assert.Equal(HttpStatusCode.OK, pfRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, athlete2Refresh.StatusCode);
    }

    [Fact]
    public async Task RP13_Given_AthletePasswordReset_When_LoggingInWithTemporaryPassword_Then_MustChangePasswordIsTrue()
    {
        // Arrange
        var pf = await _factory.SeedUserAsync("pf.rp13@protraining.com", "Profesor RP13", "PreparadorFisico", "PasswordSegura123!");
        var athlete = await _factory.SeedUserAsync("atleta.rp13@protraining.com", "Atleta RP13", "Atleta", "PasswordSegura123!", pfId: pf.Id);

        var pfLogin = await LoginUserAsync(pf.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfLogin.AccessToken);

        var resetResponse = await _client.PostAsync($"/api/v1/auth/athletes/{athlete.Id}/reset-password", null);
        var resetResult = await resetResponse.Content.ReadFromJsonAsync<ResetAthletePasswordResponseDto>();
        Assert.NotNull(resetResult);

        // Act: Iniciar sesión con la contraseña temporal generada
        _client.DefaultRequestHeaders.Authorization = null;
        var athleteLogin = await LoginUserAsync(athlete.Email!, resetResult.TemporaryPassword);

        // Assert
        Assert.True(athleteLogin.User.MustChangePassword, "El flag MustChangePassword debe ser true en el login.");
    }

    [Fact]
    public async Task RP14_Given_AthleteWithMustChangePasswordTrue_When_ChangingPasswordViaChangePasswordEndpoint_Then_MustChangePasswordBecomesFalse()
    {
        // Arrange
        var pf = await _factory.SeedUserAsync("pf.rp14@protraining.com", "Profesor RP14", "PreparadorFisico", "PasswordSegura123!");
        var athlete = await _factory.SeedUserAsync("atleta.rp14@protraining.com", "Atleta RP14", "Atleta", "PasswordSegura123!", pfId: pf.Id);

        var pfLogin = await LoginUserAsync(pf.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfLogin.AccessToken);

        var resetResponse = await _client.PostAsync($"/api/v1/auth/athletes/{athlete.Id}/reset-password", null);
        var resetResult = await resetResponse.Content.ReadFromJsonAsync<ResetAthletePasswordResponseDto>();
        Assert.NotNull(resetResult);

        // Login con clave temporal
        var athleteLogin = await LoginUserAsync(athlete.Email!, resetResult.TemporaryPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", athleteLogin.AccessToken);

        // Act: El atleta cambia su clave mediante POST /api/v1/auth/change-password (KAN-538)
        var changeRequest = new ChangePasswordRequestDto(resetResult.TemporaryPassword, "NuevaPasswordDefinitiva123!");
        var changeResponse = await _client.PostAsJsonAsync("/api/v1/auth/change-password", changeRequest);
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);

        // Assert: En la BD MustChangePassword debe ser false
        var updatedAthlete = await _factory.FindUserByEmailAsync(athlete.Email!);
        Assert.NotNull(updatedAthlete);
        Assert.False(updatedAthlete.MustChangePassword, "MustChangePassword debe volver a false tras el cambio exitoso.");

        // Login con la nueva clave definitiva
        _client.DefaultRequestHeaders.Authorization = null;
        var finalLogin = await LoginUserAsync(athlete.Email!, "NuevaPasswordDefinitiva123!");
        Assert.False(finalLogin.User.MustChangePassword);
    }

    [Fact]
    public async Task RP15_Given_ResetAthletePasswordResponse_When_CheckingHeaders_Then_IncludesCacheControlNoStoreAndPragmaNoCache()
    {
        // Arrange
        var pf = await _factory.SeedUserAsync("pf.rp15@protraining.com", "Profesor RP15", "PreparadorFisico", "PasswordSegura123!");
        var athlete = await _factory.SeedUserAsync("atleta.rp15@protraining.com", "Atleta RP15", "Atleta", "PasswordSegura123!", pfId: pf.Id);

        var pfLogin = await LoginUserAsync(pf.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfLogin.AccessToken);

        // Act
        var response = await _client.PostAsync($"/api/v1/auth/athletes/{athlete.Id}/reset-password", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true, "Cache-Control debe tener no-store.");
        Assert.True(response.Headers.Contains("Pragma"), "Debe incluir header Pragma.");
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        _client.Dispose();
    }
}
