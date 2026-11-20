using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
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
        bool mustChangePassword = false,
        bool emailConfirmed = true)
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
            EmailConfirmed = emailConfirmed
        };

        var result = await userManager.CreateAsync(user, "Password123!");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, role);
        return user;
    }

    private async Task LinkGoogleDirectlyAsync(Guid userId, string googleSubject)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        var res = await userManager.AddLoginAsync(user, new UserLoginInfo("Google", googleSubject, "Google"));
        Assert.True(res.Succeeded);
    }

    private async Task<LoginResponseDto> LoginTraditionalAsync(string email, string password = "Password123!")
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(email, password, "device-trad"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(body);
        return body;
    }

    [Fact]
    public async Task GL01_GoogleLogin_UnlinkedExistingUser_RejectsAndDoesNotAutoLink()
    {
        // GIVEN un usuario existente SIN vínculo explícito previo de Google
        var user = await CreateUserAsync("atleta.gl01@gmail.com", "Atleta", pfId: Guid.NewGuid());
        const string fakeToken = "valid-google-token-gl01";
        const string googleSub = "google-sub-1001";

        _factory.GoogleTokenValidator.RegisterToken(fakeToken, new GoogleTokenPayload(
            Subject: googleSub,
            Email: user.Email!,
            EmailVerified: true,
            Name: "Atleta GL01"
        ));

        // WHEN intenta iniciar sesión con Google sin haber vinculado antes
        var response = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto(fakeToken, "device-gl01"));

        // THEN es rechazado con 401 Unauthorized y NO crea vínculo automático
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var linkedUser = await userManager.FindByLoginAsync("Google", googleSub);
        Assert.Null(linkedUser);
    }

    [Fact]
    public async Task GL02_GoogleLogin_LinkedUser_ReturnsValidJwtAndRefreshToken()
    {
        // GIVEN un PF existente con vínculo explícito de Google
        var pf = await CreateUserAsync("pf.linked@gmail.com", "PreparadorFisico");
        const string fakeToken = "valid-google-token-gl02";
        const string googleSub = "google-sub-linked-pf";
        await LinkGoogleDirectlyAsync(pf.Id, googleSub);

        _factory.GoogleTokenValidator.RegisterToken(fakeToken, new GoogleTokenPayload(
            Subject: googleSub,
            Email: pf.Email!,
            EmailVerified: true
        ));

        // WHEN inicia sesión con Google
        var response = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto(fakeToken, "device-gl02"));

        // THEN recibe 200 OK con JWT y RefreshToken válidos
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(body.RefreshToken));
        Assert.Equal("PreparadorFisico", body.User.Role);
        Assert.Equal(pf.Id, body.User.PfId);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.AccessToken);
        Assert.Equal("PreparadorFisico", jwt.Claims.First(c => c.Type == "role").Value);
        Assert.Equal(pf.Id.ToString(), jwt.Claims.First(c => c.Type == "pf_id").Value);
        Assert.Equal(pf.Id.ToString(), jwt.Claims.First(c => c.Type == "sub").Value);
    }

    [Fact]
    public async Task GL03_GoogleLogin_NonExistentUser_RejectsAndDoesNotCreateUser()
    {
        const string fakeToken = "valid-google-token-unknown";
        const string unknownEmail = "desconocido@gmail.com";

        _factory.GoogleTokenValidator.RegisterToken(fakeToken, new GoogleTokenPayload(
            Subject: "google-sub-unknown",
            Email: unknownEmail,
            EmailVerified: true,
            Name: "Desconocido"
        ));

        var response = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto(fakeToken, "device-gl03"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        Assert.Null(await userManager.FindByEmailAsync(unknownEmail));
    }

    [Fact]
    public async Task GL04_GoogleLogin_InvalidOrUnverifiedToken_ReturnsUnauthorized()
    {
        var invalidResponse = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto("bad-token", "device-gl04a"));
        Assert.Equal(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);

        var user = await CreateUserAsync("unverified@gmail.com", "Atleta");
        await LinkGoogleDirectlyAsync(user.Id, "google-sub-unverified");
        _factory.GoogleTokenValidator.RegisterToken("unverified-email-token", new GoogleTokenPayload(
            Subject: "google-sub-unverified",
            Email: user.Email!,
            EmailVerified: false
        ));

        var unverifiedResponse = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto("unverified-email-token", "device-gl04b"));
        Assert.Equal(HttpStatusCode.Unauthorized, unverifiedResponse.StatusCode);
    }

    [Fact]
    public async Task GL05_GoogleLogin_RoleAndPfIsolation_MaintainsProTrainingInternalData()
    {
        var pfId = Guid.NewGuid();
        var athlete = await CreateUserAsync("atleta.isolated@gmail.com", "Atleta", pfId: pfId);
        await LinkGoogleDirectlyAsync(athlete.Id, "google-sub-gl05");

        const string fakeToken = "valid-token-gl05";
        _factory.GoogleTokenValidator.RegisterToken(fakeToken, new GoogleTokenPayload(
            Subject: "google-sub-gl05",
            Email: athlete.Email!,
            EmailVerified: true,
            Name: "Google Fake Name"
        ));

        var response = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto(fakeToken, "device-gl05"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(body);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.AccessToken);
        Assert.Equal("Atleta", jwt.Claims.First(c => c.Type == "role").Value);
        Assert.Equal(pfId.ToString(), jwt.Claims.First(c => c.Type == "pf_id").Value);
        Assert.Equal(athlete.Id.ToString(), jwt.Claims.First(c => c.Type == "sub").Value);
    }

    [Fact]
    public async Task GL06_GoogleLogin_MustChangePassword_EmitsClaimInJwt()
    {
        var athlete = await CreateUserAsync("mustchange@gmail.com", "Atleta", mustChangePassword: true);
        await LinkGoogleDirectlyAsync(athlete.Id, "google-sub-mustchange");

        const string fakeToken = "token-mustchange";
        _factory.GoogleTokenValidator.RegisterToken(fakeToken, new GoogleTokenPayload(
            Subject: "google-sub-mustchange",
            Email: athlete.Email!,
            EmailVerified: true
        ));

        var response = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto(fakeToken, "device-gl06"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(body);
        Assert.True(body.User.MustChangePassword);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.AccessToken);
        Assert.Equal("true", jwt.Claims.First(c => c.Type == "must_change_password").Value);
    }

    [Fact]
    public async Task GL07_GoogleLogin_PfWithUnconfirmedEmail_ReturnsForbidden()
    {
        var pf = await CreateUserAsync("pf.unconfirmed@gmail.com", "PreparadorFisico", emailConfirmed: false);
        await LinkGoogleDirectlyAsync(pf.Id, "google-sub-unconfirmed-pf");

        const string fakeToken = "token-unconfirmed-pf";
        _factory.GoogleTokenValidator.RegisterToken(fakeToken, new GoogleTokenPayload(
            Subject: "google-sub-unconfirmed-pf",
            Email: pf.Email!,
            EmailVerified: true
        ));

        var response = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto(fakeToken, "device-gl07"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GL08_GoogleLogin_InactiveAndLockedAccount_IsRejected()
    {
        var inactiveUser = await CreateUserAsync("inactive@gmail.com", "Atleta", isActive: false);
        await LinkGoogleDirectlyAsync(inactiveUser.Id, "google-sub-inactive");
        _factory.GoogleTokenValidator.RegisterToken("token-inactive", new GoogleTokenPayload(
            Subject: "google-sub-inactive",
            Email: inactiveUser.Email!,
            EmailVerified: true
        ));

        var inactiveResponse = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto("token-inactive", "device-gl08a"));
        Assert.Equal(HttpStatusCode.Unauthorized, inactiveResponse.StatusCode);

        var lockedUser = await CreateUserAsync("locked@gmail.com", "Atleta", isActive: true);
        await LinkGoogleDirectlyAsync(lockedUser.Id, "google-sub-locked");
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var u = await userManager.FindByIdAsync(lockedUser.Id.ToString());
            await userManager.SetLockoutEnabledAsync(u!, true);
            await userManager.SetLockoutEndDateAsync(u!, DateTimeOffset.UtcNow.AddMinutes(30));
        }

        _factory.GoogleTokenValidator.RegisterToken("token-locked", new GoogleTokenPayload(
            Subject: "google-sub-locked",
            Email: lockedUser.Email!,
            EmailVerified: true
        ));

        var lockedResponse = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequestDto("token-locked", "device-gl08b"));
        Assert.Equal(HttpStatusCode.Unauthorized, lockedResponse.StatusCode);
    }

    [Fact]
    public async Task GL09_ExplicitLinkStatusAndUnlink_FullSecureLifecycle()
    {
        // GIVEN un usuario autenticado con email y contraseña
        var user = await CreateUserAsync("link.flow@gmail.com", "Atleta", pfId: Guid.NewGuid());
        var otherUser = await CreateUserAsync("other.user@gmail.com", "Atleta", pfId: Guid.NewGuid());
        var session = await LoginTraditionalAsync(user.Email!);

        var authedClient = _factory.CreateClient();
        authedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        // 0. Sin autenticar -> 401
        var unauthStatus = await _client.GetAsync("/api/v1/auth/google/link");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthStatus.StatusCode);

        // 1. Estado inicial: false
        var initialStatus = await authedClient.GetFromJsonAsync<GoogleLinkStatusResponseDto>("/api/v1/auth/google/link");
        Assert.NotNull(initialStatus);
        Assert.False(initialStatus.GoogleLinked);

        const string validGoogleToken = "token-link-flow";
        const string googleSub = "google-sub-link-flow";
        _factory.GoogleTokenValidator.RegisterToken(validGoogleToken, new GoogleTokenPayload(
            Subject: googleSub,
            Email: user.Email!,
            EmailVerified: true
        ));

        // 2. Sin contraseña o contraseña incorrecta -> 401
        var wrongPasswordRes = await authedClient.PostAsJsonAsync("/api/v1/auth/google/link",
            new LinkGoogleAccountRequestDto(validGoogleToken, "WrongPass123!"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordRes.StatusCode);

        // 3. Correo diferente -> 400
        _factory.GoogleTokenValidator.RegisterToken("token-diff-email", new GoogleTokenPayload(
            Subject: "google-sub-diff",
            Email: "otro.correo@gmail.com",
            EmailVerified: true
        ));
        var diffEmailRes = await authedClient.PostAsJsonAsync("/api/v1/auth/google/link",
            new LinkGoogleAccountRequestDto("token-diff-email", "Password123!"));
        Assert.Equal(HttpStatusCode.BadRequest, diffEmailRes.StatusCode);

        // 4. Vinculación válida -> 200 (googleLinked: true)
        var linkRes = await authedClient.PostAsJsonAsync("/api/v1/auth/google/link",
            new LinkGoogleAccountRequestDto(validGoogleToken, "Password123!"));
        Assert.Equal(HttpStatusCode.OK, linkRes.StatusCode);
        var linkBody = await linkRes.Content.ReadFromJsonAsync<GoogleLinkStatusResponseDto>();
        Assert.True(linkBody?.GoogleLinked);

        // 5. Vinculación repetida mismo Subject -> idempotente 200
        var repeatRes = await authedClient.PostAsJsonAsync("/api/v1/auth/google/link",
            new LinkGoogleAccountRequestDto(validGoogleToken, "Password123!"));
        Assert.Equal(HttpStatusCode.OK, repeatRes.StatusCode);

        // 6. Otra cuenta intenta vincular el mismo Google Subject -> 409 Conflict
        var otherSession = await LoginTraditionalAsync(otherUser.Email!);
        var otherClient = _factory.CreateClient();
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherSession.AccessToken);
        _factory.GoogleTokenValidator.RegisterToken("token-hijack", new GoogleTokenPayload(
            Subject: googleSub,
            Email: otherUser.Email!,
            EmailVerified: true
        ));
        var dupSubRes = await otherClient.PostAsJsonAsync("/api/v1/auth/google/link",
            new LinkGoogleAccountRequestDto("token-hijack", "Password123!"));
        Assert.Equal(HttpStatusCode.Conflict, dupSubRes.StatusCode);

        // 7. Ahora sí puede iniciar sesión con Google
        var googleLoginRes = await _client.PostAsJsonAsync("/api/v1/auth/google",
            new GoogleLoginRequestDto(validGoogleToken, "device-after-link"));
        Assert.Equal(HttpStatusCode.OK, googleLoginRes.StatusCode);

        // 8. Desvincular con contraseña correcta -> 200 (googleLinked: false), mantiene usuario y login tradicional
        var unlinkReq = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/auth/google/link")
        {
            Content = JsonContent.Create(new UnlinkGoogleAccountRequestDto("Password123!"))
        };
        var unlinkRes = await authedClient.SendAsync(unlinkReq);
        Assert.Equal(HttpStatusCode.OK, unlinkRes.StatusCode);
        var unlinkBody = await unlinkRes.Content.ReadFromJsonAsync<GoogleLinkStatusResponseDto>();
        Assert.False(unlinkBody?.GoogleLinked);

        // Login tradicional sigue funcionando
        await LoginTraditionalAsync(user.Email!);

        // Login con Google vuelve a rechazarse
        var afterUnlinkGoogle = await _client.PostAsJsonAsync("/api/v1/auth/google",
            new GoogleLoginRequestDto(validGoogleToken, "device-after-unlink"));
        Assert.Equal(HttpStatusCode.Unauthorized, afterUnlinkGoogle.StatusCode);
    }
}
