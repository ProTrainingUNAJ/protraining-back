using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.DTOs.Auth;
using Identidad.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Identidad.Tests.Integration;

[Trait("Category", "Security")]
public sealed class ChangePasswordTests : IAsyncDisposable
{
    private readonly IdentidadTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public ChangePasswordTests()
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
    public async Task CP01_Given_AuthenticatedUser_When_CurrentPasswordCorrectAndNewPasswordValid_Then_Returns204_AndHashIsUpdated()
    {
        // Arrange
        var user = await _factory.SeedUserAsync("atleta.cp01@protraining.com", "Atleta CP01", "Atleta", "PasswordSegura123!");
        var originalHash = user.PasswordHash;

        var login = await LoginUserAsync(user.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var request = new ChangePasswordRequestDto("PasswordSegura123!", "NuevaPasswordSegura456!");

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/change-password", request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updatedUser = await _factory.FindUserByEmailAsync(user.Email!);
        Assert.NotNull(updatedUser);
        Assert.NotEqual(originalHash, updatedUser.PasswordHash);
    }

    [Fact]
    public async Task CP02_Given_PasswordChanged_When_LoginWithNewPassword_Then_SucceedsAndIssuesTokens()
    {
        // Arrange
        var user = await _factory.SeedUserAsync("atleta.cp02@protraining.com", "Atleta CP02", "Atleta", "PasswordSegura123!");
        var login = await LoginUserAsync(user.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        await _client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequestDto("PasswordSegura123!", "NuevaPasswordSegura456!"));

        // Act: Intentar login con la nueva contraseña
        _client.DefaultRequestHeaders.Authorization = null;
        var newLoginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(user.Email!, "NuevaPasswordSegura456!", "device-galaxy-s24"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, newLoginResponse.StatusCode);
        var newLoginResult = await newLoginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(newLoginResult);
        Assert.False(string.IsNullOrWhiteSpace(newLoginResult.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(newLoginResult.RefreshToken));
    }

    [Fact]
    public async Task CP03_Given_PasswordChanged_When_LoginWithOldPassword_Then_RejectedWith401Unauthorized()
    {
        // Arrange
        var user = await _factory.SeedUserAsync("atleta.cp03@protraining.com", "Atleta CP03", "Atleta", "PasswordSegura123!");
        var login = await LoginUserAsync(user.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        await _client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequestDto("PasswordSegura123!", "NuevaPasswordSegura456!"));

        // Act: Intentar login con la contraseña vieja
        _client.DefaultRequestHeaders.Authorization = null;
        var oldLoginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(user.Email!, "PasswordSegura123!", "device-galaxy-s24"));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, oldLoginResponse.StatusCode);
    }

    [Fact]
    public async Task CP04_Given_IncorrectCurrentPassword_When_ChangePassword_Then_Returns400_WithSpanishMessage_AndDoesNotModifyPassword()
    {
        // Arrange
        var user = await _factory.SeedUserAsync("atleta.cp04@protraining.com", "Atleta CP04", "Atleta", "PasswordSegura123!");
        var originalHash = user.PasswordHash;

        var login = await LoginUserAsync(user.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var request = new ChangePasswordRequestDto("PasswordIncorrecta999!", "NuevaPasswordSegura456!");

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/change-password", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("La contraseña actual es incorrecta.", problem.Title);

        var userInDb = await _factory.FindUserByEmailAsync(user.Email!);
        Assert.NotNull(userInDb);
        Assert.Equal(originalHash, userInDb.PasswordHash);

        // Refresh token debe seguir funcionando
        var refreshResponse = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequestDto(login.RefreshToken, "device-test-1"));
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
    }

    [Theory]
    [InlineData("corta1!", "Menos de 8 caracteres")]
    [InlineData("sololetras", "Sin número")]
    [InlineData("", "Contraseña vacía")]
    [InlineData("PasswordSegura123!", "Igual a la actual")]
    public async Task CP05_Given_InvalidNewPassword_When_ChangePassword_Then_Returns400_AndDoesNotModifyPassword(string invalidNewPassword, string reason)
    {
        Assert.False(string.IsNullOrWhiteSpace(reason));
        // Arrange
        var user = await _factory.SeedUserAsync($"atleta.{Guid.NewGuid():N}@protraining.com", "Atleta Test", "Atleta", "PasswordSegura123!");
        var originalHash = user.PasswordHash;

        var login = await LoginUserAsync(user.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var request = new ChangePasswordRequestDto("PasswordSegura123!", invalidNewPassword);

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/change-password", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var userInDb = await _factory.FindUserByEmailAsync(user.Email!);
        Assert.NotNull(userInDb);
        Assert.Equal(originalHash, userInDb.PasswordHash);
    }

    [Theory]
    [InlineData("passwordsegura1", "Sin mayúscula y sin carácter especial")]
    [InlineData("PASSWORDSEGURA123", "Sin minúscula y sin carácter especial")]
    [InlineData("protrain8", "Exactamente 8 caracteres con número")]
    public async Task CP05b_Given_UnifiedPasswordPolicy_When_PasswordHas8CharsAndDigit_Then_Succeeds(string validNewPassword, string reason)
    {
        Assert.False(string.IsNullOrWhiteSpace(reason));
        // Arrange
        var user = await _factory.SeedUserAsync($"atleta.{Guid.NewGuid():N}@protraining.com", "Atleta Test", "Atleta", "PasswordSegura123!");
        var originalHash = user.PasswordHash;

        var login = await LoginUserAsync(user.Email!, "PasswordSegura123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var request = new ChangePasswordRequestDto("PasswordSegura123!", validNewPassword);

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/change-password", request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var userInDb = await _factory.FindUserByEmailAsync(user.Email!);
        Assert.NotNull(userInDb);
        Assert.NotEqual(originalHash, userInDb.PasswordHash);
    }

    [Fact]
    public async Task CP06_Given_UnauthenticatedUser_When_ChangePassword_Then_Returns401Unauthorized()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = null;
        var request = new ChangePasswordRequestDto("PasswordSegura123!", "NuevaPasswordSegura456!");

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/change-password", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CP07_Given_ValidJwt_When_ChangePassword_Then_OnlyModifiesUserIdentifiedByTokenClaim()
    {
        // Arrange
        var userA = await _factory.SeedUserAsync("user.a@protraining.com", "Usuario A", "Atleta", "PasswordA123!");
        var userB = await _factory.SeedUserAsync("user.b@protraining.com", "Usuario B", "Atleta", "PasswordB123!");
        var hashBBefore = userB.PasswordHash;

        var loginA = await LoginUserAsync(userA.Email!, "PasswordA123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginA.AccessToken);

        // Act: User A cambia contraseña
        var response = await _client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequestDto("PasswordA123!", "NuevaPasswordA456!"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Assert: Usuario B no fue alterado
        var userBAfter = await _factory.FindUserByEmailAsync(userB.Email!);
        Assert.NotNull(userBAfter);
        Assert.Equal(hashBBefore, userBAfter.PasswordHash);
    }

    [Fact]
    public async Task CP08_Given_MultipleActiveSessions_When_PasswordChanged_Then_AllActiveRefreshTokensAreRevoked()
    {
        // Arrange: Crear dos sesiones en distintos dispositivos para el mismo usuario
        var user = await _factory.SeedUserAsync("atleta.cp08@protraining.com", "Atleta CP08", "Atleta", "PasswordSegura123!");

        var session1 = await LoginUserAsync(user.Email!, "PasswordSegura123!", "device-phone-1");
        var session2 = await LoginUserAsync(user.Email!, "PasswordSegura123!", "device-laptop-2");

        // User cambia contraseña desde session1
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session1.AccessToken);
        var changeResponse = await _client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequestDto("PasswordSegura123!", "NuevaPasswordSegura456!"));
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);

        // Act & Assert: Intentar refrescar desde device-phone-1 -> rechazado
        _client.DefaultRequestHeaders.Authorization = null;
        var refresh1 = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequestDto(session1.RefreshToken, "device-phone-1"));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh1.StatusCode);

        // Intentar refrescar desde device-laptop-2 -> rechazado
        var refresh2 = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequestDto(session2.RefreshToken, "device-laptop-2"));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh2.StatusCode);
    }

    [Fact]
    public async Task CP09_Given_TwoUsers_When_UserAChangesPassword_Then_UserBTokensRemainValidAndUnchanged()
    {
        // Arrange
        var userA = await _factory.SeedUserAsync("atleta.cp09.a@protraining.com", "Atleta A", "Atleta", "PasswordA123!");
        var userB = await _factory.SeedUserAsync("atleta.cp09.b@protraining.com", "Atleta B", "Atleta", "PasswordB123!");

        var sessionA = await LoginUserAsync(userA.Email!, "PasswordA123!", "device-a");
        var sessionB = await LoginUserAsync(userB.Email!, "PasswordB123!", "device-b");

        // User A cambia su contraseña
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionA.AccessToken);
        var changeResponse = await _client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequestDto("PasswordA123!", "NuevaPasswordA456!"));
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);

        // Act: User B intenta renovar token
        _client.DefaultRequestHeaders.Authorization = null;
        var refreshB = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequestDto(sessionB.RefreshToken, "device-b"));

        // Assert: Sesión de User B sigue completamente válida
        Assert.Equal(HttpStatusCode.OK, refreshB.StatusCode);
        var refreshResultB = await refreshB.Content.ReadFromJsonAsync<RefreshResponseDto>();
        Assert.NotNull(refreshResultB);
        Assert.False(string.IsNullOrWhiteSpace(refreshResultB.AccessToken));

        // Y User B sigue pudiendo iniciar sesión con su contraseña habitual
        var loginB = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(userB.Email!, "PasswordB123!", "device-b2"));
        Assert.Equal(HttpStatusCode.OK, loginB.StatusCode);
    }

    [Fact]
    public async Task CP10_Given_PasswordChange_When_InspectingDatabase_Then_PersistenceIsConsistent()
    {
        // Arrange
        var user = await _factory.SeedUserAsync("atleta.cp10@protraining.com", "Atleta CP10", "Atleta", "PasswordSegura123!");
        var session = await LoginUserAsync(user.Email!, "PasswordSegura123!", "device-db");
        var originalSecurityStamp = user.SecurityStamp;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequestDto("PasswordSegura123!", "NuevaPasswordSegura456!"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Assert
        var userInDb = await _factory.FindUserByEmailAsync(user.Email!);
        Assert.NotNull(userInDb);
        Assert.NotEqual(originalSecurityStamp, userInDb.SecurityStamp);
    }

    [Fact]
    public async Task CP11_Given_BothPfAndAthlete_When_ChangingOwnPassword_Then_BothSucceed()
    {
        // Arrange: PF
        var pf = await _factory.SeedUserAsync("pf.cp11@protraining.com", "PF CP11", "PreparadorFisico", "PasswordPf123!");
        var sessionPf = await LoginUserAsync(pf.Email!, "PasswordPf123!", "device-pf");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionPf.AccessToken);

        var responsePf = await _client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequestDto("PasswordPf123!", "NuevaPasswordPf456!"));
        Assert.Equal(HttpStatusCode.NoContent, responsePf.StatusCode);

        // Arrange: Atleta
        var atleta = await _factory.SeedUserAsync("atleta.cp11@protraining.com", "Atleta CP11", "Atleta", "PasswordAtleta123!");
        var sessionAtleta = await LoginUserAsync(atleta.Email!, "PasswordAtleta123!", "device-atleta");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionAtleta.AccessToken);

        var responseAtleta = await _client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequestDto("PasswordAtleta123!", "NuevaPasswordAtleta456!"));
        Assert.Equal(HttpStatusCode.NoContent, responseAtleta.StatusCode);
    }

    [Fact]
    public async Task CP12_Given_ConcurrentOrPostChangeRefresh_When_UsingOldRefreshToken_Then_RejectedImmediately()
    {
        // Arrange
        var user = await _factory.SeedUserAsync("atleta.cp12@protraining.com", "Atleta CP12", "Atleta", "PasswordSegura123!");
        var session = await LoginUserAsync(user.Email!, "PasswordSegura123!", "device-concurrent");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        await _client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequestDto("PasswordSegura123!", "NuevaPasswordSegura456!"));

        // Act: Refresh con el token previo revocado
        _client.DefaultRequestHeaders.Authorization = null;
        var refreshResponse = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequestDto(session.RefreshToken, "device-concurrent"));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}
