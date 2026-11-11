using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Auth;
using Application.Interfaces.Services;
using Domain.Entities;
using Identidad.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Identidad.Tests.Integration;

public class GoogleLoginTests : IAsyncLifetime
{
    private readonly IdentidadTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public GoogleLoginTests()
    {
        _factory = new IdentidadTestApplicationFactory();
        _client = _factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task<User> CreateUserAsync(
        string email,
        string role,
        Guid? pfId = null,
        bool isActive = true,
        bool mustChangePassword = false)
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
            UserName = email,
            Email = email,
            FullName = "Usuario Test " + role,
            PfId = pfId,
            IsActive = isActive,
            MustChangePassword = mustChangePassword,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, "Password123!");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, role);
        return user;
    }

    [Fact]
    public async Task GL01_GoogleLogin_ExistingUser_LinksAndReturnsValidSession()
    {
        // GIVEN un usuario existente en ProTraining
        var user = await CreateUserAsync("atleta.gl01@gmail.com", "Atleta", pfId: Guid.NewGuid());
        const string fakeToken = "valid-google-token-gl01";
        const string googleSub = "google-sub-1001";

        _factory.GoogleTokenValidator.RegisterToken(fakeToken, new GoogleTokenPayload(
            Subject: googleSub,
            Email: user.Email!,
            EmailVerified: true,
            Name: "Atleta GL01"
        ));

        // WHEN inicia sesión con Google
        var response = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto(fakeToken, "device-gl01"));

        // THEN recibe HTTP 200 con tokens válidos
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(body.RefreshToken));
        Assert.Equal("Atleta", body.User.Role);
        Assert.Equal(user.Email, body.User.Email);

        // Y la cuenta quedó vinculada en user_logins
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var linkedUser = await userManager.FindByLoginAsync("Google", googleSub);
        Assert.NotNull(linkedUser);
        Assert.Equal(user.Id, linkedUser.Id);
    }

    [Fact]
    public async Task GL02_GoogleLogin_NonExistentUser_RejectsAndDoesNotCreateUser()
    {
        // GIVEN una cuenta de Google sin usuario previo en ProTraining
        const string fakeToken = "valid-google-token-unknown";
        const string unknownEmail = "desconocido@gmail.com";
        const string googleSub = "google-sub-unknown";

        _factory.GoogleTokenValidator.RegisterToken(fakeToken, new GoogleTokenPayload(
            Subject: googleSub,
            Email: unknownEmail,
            EmailVerified: true,
            Name: "Desconocido"
        ));

        // WHEN intenta iniciar sesión con Google
        var response = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto(fakeToken, "device-gl02"));

        // THEN se rechaza con 401 Unauthorized
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Y NO se crea ninguna cuenta en la base de datos
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByEmailAsync(unknownEmail);
        Assert.Null(user);
    }

    [Fact]
    public async Task GL03_GoogleLogin_InvalidToken_ReturnsUnauthorized()
    {
        // GIVEN un token de Google adulterado o no registrado
        const string badToken = "invalid-or-expired-token";

        // WHEN intenta autenticar
        var response = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto(badToken, "device-gl03"));

        // THEN responde HTTP 401 Unauthorized
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GL04_GoogleLogin_UnverifiedEmail_ReturnsUnauthorized()
    {
        // GIVEN un token de Google con email_verified == false
        var user = await CreateUserAsync("unverified@gmail.com", "Atleta");
        const string fakeToken = "unverified-email-token";

        _factory.GoogleTokenValidator.RegisterToken(fakeToken, new GoogleTokenPayload(
            Subject: "google-sub-unverified",
            Email: user.Email!,
            EmailVerified: false,
            Name: "Unverified"
        ));

        // WHEN intenta iniciar sesión
        var response = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto(fakeToken, "device-gl04"));

        // THEN es rechazado con 401 Unauthorized
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GL05_GoogleLogin_RoleAndPfIsolation_MaintainsProTrainingInternalData()
    {
        // GIVEN un atleta y su PF asignado
        var pfId = Guid.NewGuid();
        var athlete = await CreateUserAsync("atleta.isolated@gmail.com", "Atleta", pfId: pfId);

        const string fakeToken = "valid-token-gl05";
        _factory.GoogleTokenValidator.RegisterToken(fakeToken, new GoogleTokenPayload(
            Subject: "google-sub-gl05",
            Email: athlete.Email!,
            EmailVerified: true,
            Name: "Google Fake Name"
        ));

        // WHEN se autentica con Google
        var response = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto(fakeToken, "device-gl05"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(body);

        // THEN el JWT contiene el rol "Atleta" y su pf_id real
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(body.AccessToken);

        Assert.Equal("Atleta", jwt.Claims.First(c => c.Type == "role").Value);
        Assert.Equal(pfId.ToString(), jwt.Claims.First(c => c.Type == "pf_id").Value);
        Assert.Equal(athlete.Id.ToString(), jwt.Claims.First(c => c.Type == "sub").Value);
    }

    [Fact]
    public async Task GL06_GoogleLogin_MustChangePassword_EmitsClaimInJwt()
    {
        // GIVEN un atleta con cambio obligatorio de contraseña
        var athlete = await CreateUserAsync("mustchange@gmail.com", "Atleta", mustChangePassword: true);

        const string fakeToken = "token-mustchange";
        _factory.GoogleTokenValidator.RegisterToken(fakeToken, new GoogleTokenPayload(
            Subject: "google-sub-mustchange",
            Email: athlete.Email!,
            EmailVerified: true
        ));

        // WHEN inicia sesión con Google
        var response = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto(fakeToken, "device-gl06"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(body);
        Assert.True(body.User.MustChangePassword);

        // THEN el JWT incluye el claim must_change_password
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(body.AccessToken);
        Assert.Equal("true", jwt.Claims.First(c => c.Type == "must_change_password").Value);
    }

    [Fact]
    public async Task GL07_GoogleLogin_ConflictingIdentity_RejectsLinking()
    {
        // GIVEN un usuario ya vinculado a la cuenta Google sub-A
        var user = await CreateUserAsync("user.conflict@gmail.com", "Atleta");
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var userInScope = await userManager.FindByEmailAsync(user.Email!);
            await userManager.AddLoginAsync(userInScope!, new UserLoginInfo("Google", "sub-A", "Google"));
        }

        // WHEN intenta loguearse con una identidad de Google sub-B pero con el mismo email
        const string fakeTokenB = "token-sub-B";
        _factory.GoogleTokenValidator.RegisterToken(fakeTokenB, new GoogleTokenPayload(
            Subject: "sub-B",
            Email: user.Email!,
            EmailVerified: true
        ));

        var response = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto(fakeTokenB, "device-gl07"));

        // THEN responde 401 Unauthorized por conflicto de identidad
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GL08_GoogleLogin_InactiveAccount_IsRejected()
    {
        // GIVEN un usuario inactivo
        var user = await CreateUserAsync("inactive@gmail.com", "Atleta", isActive: false);

        const string fakeToken = "token-inactive";
        _factory.GoogleTokenValidator.RegisterToken(fakeToken, new GoogleTokenPayload(
            Subject: "google-sub-inactive",
            Email: user.Email!,
            EmailVerified: true
        ));

        // WHEN intenta iniciar sesión con Google
        var response = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto(fakeToken, "device-gl08"));

        // THEN se rechaza con 401 Unauthorized
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
