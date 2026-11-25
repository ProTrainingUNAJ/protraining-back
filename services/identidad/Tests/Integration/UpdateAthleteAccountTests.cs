using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.DTOs.Auth;
using Application.DTOs.Users;
using Identidad.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Identidad.Tests.Integration;

public sealed class UpdateAthleteAccountTests : IAsyncDisposable
{
    private readonly IdentidadTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public UpdateAthleteAccountTests()
    {
        _factory = new IdentidadTestApplicationFactory();
        _client = _factory.CreateClient();
    }

    private async Task<LoginResponseDto> LoginUserAsync(string email, string password, string deviceId = "device-test-acc")
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(email, password, deviceId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(result);
        return result;
    }

    [Fact]
    public async Task Given_AuthenticatedPf_When_DisablingAthlete_Then_AthleteCannotLogin_AndReturns204()
    {
        // arrange
        var pf = await _factory.SeedUserAsync("pf.acc01@protraining.com", "PF Acc 01", "PreparadorFisico", "PasswordSegura123!");
        var athlete = await _factory.SeedUserAsync("atleta.acc01@protraining.com", "Atleta Acc 01", "Atleta", "PasswordSegura123!", pfId: pf.Id);

        var pfLogin = await LoginUserAsync(pf.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfLogin.AccessToken);

        // act: desactivar atleta
        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/users/athletes/{athlete.Id}/status",
            new UpdateAthleteUserStatusRequestDto(IsActive: false));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // assert: el atleta no puede iniciar sesion
        var clientAnom = _factory.CreateClient();
        var loginAttempt = await clientAnom.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequestDto("atleta.acc01@protraining.com", "PasswordSegura123!", "device-acc"));
        Assert.Equal(HttpStatusCode.Unauthorized, loginAttempt.StatusCode);
    }

    [Fact]
    public async Task Given_AuthenticatedPf_When_UpdatingAthleteEmail_Then_AthleteCanLoginWithNewEmail()
    {
        // arrange
        var pf = await _factory.SeedUserAsync("pf.acc02@protraining.com", "PF Acc 02", "PreparadorFisico", "PasswordSegura123!");
        var athlete = await _factory.SeedUserAsync("atleta.acc02.old@protraining.com", "Atleta Acc 02", "Atleta", "PasswordSegura123!", pfId: pf.Id);

        var pfLogin = await LoginUserAsync(pf.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfLogin.AccessToken);

        // act: actualizar correo
        var newEmail = "atleta.acc02.new@protraining.com";
        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/users/athletes/{athlete.Id}/email",
            new UpdateAthleteUserEmailRequestDto(Email: newEmail));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // assert: puede loguearse con el nuevo correo
        var clientAnom = _factory.CreateClient();
        var loginResponse = await clientAnom.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequestDto(newEmail, "PasswordSegura123!", "device-acc-new"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task Given_AuthenticatedPf_When_UpdatingAthletePhone_Then_PhoneNumberIsUpdatedInDatabase()
    {
        // arrange
        var pf = await _factory.SeedUserAsync("pf.acc03@protraining.com", "PF Acc 03", "PreparadorFisico", "PasswordSegura123!");
        var athlete = await _factory.SeedUserAsync("atleta.acc03@protraining.com", "Atleta Acc 03", "Atleta", "PasswordSegura123!", pfId: pf.Id);

        var pfLogin = await LoginUserAsync(pf.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfLogin.AccessToken);

        // act: actualizar telefono
        var newPhone = "+5491122334455";
        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/users/athletes/{athlete.Id}/phone",
            new Application.DTOs.Users.UpdateAthleteUserPhoneRequestDto(PhoneNumber: newPhone));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // assert: verificado en base de datos
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Domain.Entities.User>>();
        var dbAthlete = await userManager.FindByIdAsync(athlete.Id.ToString());
        Assert.NotNull(dbAthlete);
        Assert.Equal(newPhone, dbAthlete.PhoneNumber);
    }

    [Fact]
    public async Task Given_DisabledAthleteWithLockout_When_ReactivatingAthlete_Then_AthleteCanLoginImmediately_AndLockoutCleared()
    {
        // arrange: PF y atleta inicialmente desactivado y con Lockout activo
        var pf = await _factory.SeedUserAsync("pf.acc04@protraining.com", "PF Acc 04", "PreparadorFisico", "PasswordSegura123!");
        var athlete = await _factory.SeedUserAsync("atleta.acc04@protraining.com", "Atleta Acc 04", "Atleta", "PasswordSegura123!", pfId: pf.Id);

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Domain.Entities.User>>();
            var dbUser = await userManager.FindByIdAsync(athlete.Id.ToString());
            Assert.NotNull(dbUser);
            dbUser.IsActive = false;
            await userManager.SetLockoutEndDateAsync(dbUser, DateTimeOffset.UtcNow.AddMinutes(15));
            await userManager.AccessFailedAsync(dbUser);
            await userManager.UpdateAsync(dbUser);
        }

        var pfLogin = await LoginUserAsync(pf.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfLogin.AccessToken);

        // act: reactivar atleta
        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/users/athletes/{athlete.Id}/status",
            new UpdateAthleteUserStatusRequestDto(IsActive: true));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // assert: el atleta puede iniciar sesión de inmediato y su Lockout está limpio
        var clientAnom = _factory.CreateClient();
        var loginResponse = await clientAnom.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequestDto("atleta.acc04@protraining.com", "PasswordSegura123!", "device-acc04"));

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Domain.Entities.User>>();
            var verifiedUser = await userManager.FindByIdAsync(athlete.Id.ToString());
            Assert.NotNull(verifiedUser);
            Assert.True(verifiedUser.IsActive);
            Assert.False(await userManager.IsLockedOutAsync(verifiedUser));
            Assert.Equal(0, verifiedUser.AccessFailedCount);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
