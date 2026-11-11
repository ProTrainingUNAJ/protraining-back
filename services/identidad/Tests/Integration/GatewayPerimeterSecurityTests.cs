using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.DTOs.Auth;
using Domain.Entities;
using Gateway.Services;
using Identidad.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Forwarder;

namespace Identidad.Tests.Integration;

[Trait("Category", "Security")]
public sealed class GatewayPerimeterSecurityTests : IAsyncDisposable
{
    private readonly IdentidadTestApplicationFactory _identidadFactory;
    private readonly GatewayTestFactory _gatewayFactory;
    private readonly HttpClient _gatewayClient;

    public GatewayPerimeterSecurityTests()
    {
        _identidadFactory = new IdentidadTestApplicationFactory();
        _gatewayFactory = new GatewayTestFactory(_identidadFactory);
        _gatewayClient = _gatewayFactory.CreateClient();
    }

    private async Task<LoginResponseDto> LoginUserAsync(string email, string password)
    {
        var identidadClient = _identidadFactory.CreateClient();
        var response = await identidadClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(email, password, "test-device"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(result);
        return result;
    }

    [Fact]
    public async Task Gateway_TamperedJWT_FailsCryptographicValidation_Returns401Unauthorized()
    {
        // Arrange: Crear un JWT firmado y adulterar su firma
        var athlete = await _identidadFactory.SeedUserAsync("atleta.tamper@protraining.com", "Atleta Tamper", "Atleta");
        var login = await LoginUserAsync(athlete.Email!, "PasswordSegura123!");

        // Modificar los últimos caracteres de la firma
        var parts = login.AccessToken.Split('.');
        var tamperedSignature = parts[2].Substring(0, parts[2].Length - 4) + "XXXX";
        var tamperedToken = $"{parts[0]}.{parts[1]}.{tamperedSignature}";

        _gatewayClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tamperedToken);

        // Act: Enviar al Gateway
        var response = await _gatewayClient.GetAsync("/estructura/api/v1/athletes");

        // Assert: Rechazo criptográfico directo sin reenviar
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Gateway_JWTWithMustChangePasswordTrue_OnNormalEndpoint_Returns403Forbidden()
    {
        // Arrange: Atleta con flag MustChangePassword = true en BD
        var athlete = await _identidadFactory.SeedUserAsync(
            "atleta.mustchange@protraining.com", "Atleta MustChange", "Atleta", mustChangePassword: true);
        var login = await LoginUserAsync(athlete.Email!, "PasswordSegura123!");

        _gatewayClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        // Act: Intento de acceder a un endpoint normal a través del Gateway
        var response = await _gatewayClient.GetAsync("/estructura/api/v1/athletes");

        // Assert: El Gateway consulta Identidad y aplica el 403 Forbidden
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Gateway_JWTWithMustChangePasswordTrue_OnChangePasswordEndpoint_AllowsThrough()
    {
        // Arrange: Atleta con flag MustChangePassword = true
        var athlete = await _identidadFactory.SeedUserAsync(
            "atleta.allowchange@protraining.com", "Atleta AllowChange", "Atleta", mustChangePassword: true);
        var login = await LoginUserAsync(athlete.Email!, "PasswordSegura123!");

        _gatewayClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        // Act: El atleta invoca el endpoint de cambio de clave a través del Gateway
        var changeRequest = new ChangePasswordRequestDto("PasswordSegura123!", "NuevaPasswordSegura456!");
        var response = await _gatewayClient.PostAsJsonAsync("/identidad/api/v1/auth/change-password", changeRequest);

        // Assert: Gateway no bloquea este endpoint; la petición llega a Identidad y procesa el cambio
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Gateway_JWTIssuedPriorToReset_IsRejectedWith401Unauthorized()
    {
        // Arrange: Atleta y PF
        var pf = await _identidadFactory.SeedUserAsync("pf.gate@protraining.com", "Profesor Gate", "PreparadorFisico");
        var athlete = await _identidadFactory.SeedUserAsync("atleta.gate@protraining.com", "Atleta Gate", "Atleta", pfId: pf.Id);

        // Atleta inicia sesión y obtiene Token 1 (SecurityStamp original)
        var athleteLogin = await LoginUserAsync(athlete.Email!, "PasswordSegura123!");
        var oldToken = athleteLogin.AccessToken;

        // PF resetea la contraseña del atleta a través de Identidad (actualiza SecurityStamp en BD)
        var identidadClient = _identidadFactory.CreateClient();
        var pfLogin = await LoginUserAsync(pf.Email!, "PasswordSegura123!");
        identidadClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfLogin.AccessToken);

        var resetResponse = await identidadClient.PostAsync($"/api/v1/auth/athletes/{athlete.Id}/reset-password", null);
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        // Act: Atleta intenta usar Token 1 en el Gateway hacia Estructura
        _gatewayClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldToken);
        var response = await _gatewayClient.GetAsync("/estructura/api/v1/athletes");

        // Assert: Gateway valida con Identidad, detecta que SecurityStamp cambió y rechaza con 401
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Gateway_WhenIdentidadIsUnavailable_OperatesFailClosed_Returns503()
    {
        // Arrange: Gateway configurado hacia una dirección de Identidad inalcanzable
        var failingGatewayFactory = new GatewayFailingBackendFactory();
        var failingClient = failingGatewayFactory.CreateClient();

        var athlete = await _identidadFactory.SeedUserAsync("atleta.failclosed@protraining.com", "Atleta FailClosed", "Atleta");
        var login = await LoginUserAsync(athlete.Email!, "PasswordSegura123!");
        failingClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        // Act: Enviar petición a través del Gateway
        var response = await failingClient.GetAsync("/estructura/api/v1/athletes");

        // Assert: Fail-Closed -> Devuelve 503 sin permitir el acceso al backend
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Gateway_Restart_DoesNotLoseRevocations_StatePersistedInDatabase()
    {
        // Arrange
        var pf = await _identidadFactory.SeedUserAsync("pf.restart@protraining.com", "Profesor Restart", "PreparadorFisico");
        var athlete = await _identidadFactory.SeedUserAsync("atleta.restart@protraining.com", "Atleta Restart", "Atleta", pfId: pf.Id);

        var athleteLogin = await LoginUserAsync(athlete.Email!, "PasswordSegura123!");
        var oldToken = athleteLogin.AccessToken;

        // PF resetea contraseña en Identidad (persiste en BD)
        var identidadClient = _identidadFactory.CreateClient();
        var pfLogin = await LoginUserAsync(pf.Email!, "PasswordSegura123!");
        identidadClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pfLogin.AccessToken);
        await identidadClient.PostAsync($"/api/v1/auth/athletes/{athlete.Id}/reset-password", null);

        // Simular reinicio del Gateway levantando una nueva instancia desde cero
        using var newGatewayFactory = new GatewayTestFactory(_identidadFactory);
        var newGatewayClient = newGatewayFactory.CreateClient();
        newGatewayClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldToken);

        // Act: Petición enviada al nuevo Gateway
        var response = await newGatewayClient.GetAsync("/estructura/api/v1/athletes");

        // Assert: La nueva instancia del Gateway consulta la BD vía Identidad y mantiene el rechazo 401
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Gateway_NormalPasswordChangeViaKan538_RevokesPreviousTokens()
    {
        // Arrange: Atleta inicia sesión normalmente
        var athlete = await _identidadFactory.SeedUserAsync("atleta.kan538@protraining.com", "Atleta KAN538", "Atleta");
        var login1 = await LoginUserAsync(athlete.Email!, "PasswordSegura123!");
        var token1 = login1.AccessToken;

        // Atleta cambia su contraseña legítimamente mediante POST /api/v1/auth/change-password
        var identidadClient = _identidadFactory.CreateClient();
        identidadClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var changeResponse = await identidadClient.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequestDto("PasswordSegura123!", "NuevaPasswordSegura999!"));
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);

        // Act: Intentar usar el token1 anterior a través del Gateway
        _gatewayClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var response = await _gatewayClient.GetAsync("/estructura/api/v1/athletes");

        // Assert: El cambio legítimo de KAN-538 actualizó el SecurityStamp, por lo que token1 es 401 Unauthorized
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // El nuevo token emitido tras el cambio sí es aceptado
        var login2 = await LoginUserAsync(athlete.Email!, "NuevaPasswordSegura999!");
        _gatewayClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login2.AccessToken);
        var response2 = await _gatewayClient.GetAsync("/identidad/api/v1/auth/change-password");
        // No es 401 ni 403 (autenticación válida)
        Assert.NotEqual(HttpStatusCode.Unauthorized, response2.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response2.StatusCode);
    }

    [Fact]
    public async Task Gateway_ConsecutiveRequestsWithSameToken_UsesCache_AndCallsIdentidadOnlyOnce()
    {
        // Arrange
        var athlete = await _identidadFactory.SeedUserAsync("atleta.cache1@protraining.com", "Atleta Cache1", "Atleta");
        var login = await LoginUserAsync(athlete.Email!, "PasswordSegura123!");
        _gatewayClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var initialCalls = _gatewayFactory.CountingHandler.ValidateSessionCalls;

        // Act: Enviar primera petición
        var response1 = await _gatewayClient.GetAsync("/estructura/api/v1/athletes");
        var callsAfterFirst = _gatewayFactory.CountingHandler.ValidateSessionCalls;

        // Act: Enviar segunda petición con el mismo token (debe resolver desde caché)
        var response2 = await _gatewayClient.GetAsync("/estructura/api/v1/athletes");
        var callsAfterSecond = _gatewayFactory.CountingHandler.ValidateSessionCalls;

        // Assert: Identidad solo se llamó 1 vez en la primera petición y 0 en la segunda
        Assert.Equal(initialCalls + 1, callsAfterFirst);
        Assert.Equal(callsAfterFirst, callsAfterSecond);
    }

    [Fact]
    public async Task Gateway_EvictedCacheEntry_QueriesIdentidadAgain()
    {
        // Arrange
        var athlete = await _identidadFactory.SeedUserAsync("atleta.cache2@protraining.com", "Atleta Cache2", "Atleta");
        var login = await LoginUserAsync(athlete.Email!, "PasswordSegura123!");
        _gatewayClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        // Primera petición -> cache miss -> llamada 1
        var response1 = await _gatewayClient.GetAsync("/estructura/api/v1/athletes");
        var callsAfterFirst = _gatewayFactory.CountingHandler.ValidateSessionCalls;

        // Simular expiración de caché desalojando la clave
        var cache = _gatewayFactory.Services.GetRequiredService<IMemoryCache>();
        var userInDb = await _identidadFactory.FindUserByEmailAsync(athlete.Email!);
        var cacheKey = $"session:{athlete.Id}:{userInDb!.SecurityStamp}";
        cache.Remove(cacheKey);

        // Act: Segunda petición tras expiración
        var response2 = await _gatewayClient.GetAsync("/estructura/api/v1/athletes");
        var callsAfterSecond = _gatewayFactory.CountingHandler.ValidateSessionCalls;

        // Assert: Volvió a consultar a Identidad
        Assert.Equal(callsAfterFirst + 1, callsAfterSecond);
    }

    [Fact]
    public async Task Gateway_TokenWithDifferentSecurityStamp_DoesNotReusePreviousCacheEntry()
    {
        // Arrange: Dos atletas distintos
        var athlete1 = await _identidadFactory.SeedUserAsync("atleta.diff1@protraining.com", "Atleta Diff1", "Atleta");
        var athlete2 = await _identidadFactory.SeedUserAsync("atleta.diff2@protraining.com", "Atleta Diff2", "Atleta");

        var login1 = await LoginUserAsync(athlete1.Email!, "PasswordSegura123!");
        var login2 = await LoginUserAsync(athlete2.Email!, "PasswordSegura123!");

        // Act: Petición con Token 1
        _gatewayClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login1.AccessToken);
        await _gatewayClient.GetAsync("/estructura/api/v1/athletes");
        var calls1 = _gatewayFactory.CountingHandler.ValidateSessionCalls;

        // Act: Petición con Token 2 (diferente usuario y SecurityStamp)
        _gatewayClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login2.AccessToken);
        await _gatewayClient.GetAsync("/estructura/api/v1/athletes");
        var calls2 = _gatewayFactory.CountingHandler.ValidateSessionCalls;

        // Assert: Token 2 generó su propia consulta sin reutilizar la entrada previa
        Assert.Equal(calls1 + 1, calls2);
    }

    [Theory]
    [InlineData("Jwt:Key", "")]
    [InlineData("Jwt:Issuer", "")]
    [InlineData("Jwt:Audience", "")]
    public void Gateway_Startup_MissingMandatoryConfiguration_ThrowsInvalidOperationException(string missingKey, string emptyValue)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var dict = new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "clave_local_de_desarrollo_cambiar_32+",
                    ["Jwt:Issuer"] = "protraining-identidad",
                    ["Jwt:Audience"] = "protraining",
                    ["Services:Identidad:BaseUrl"] = "http://localhost/"
                };
                dict[missingKey] = emptyValue;
                config.AddInMemoryCollection(dict);
            });
        });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(missingKey, ex.ToString());
    }

    [Fact]
    public async Task Gateway_PublicEndpoints_ForgotPasswordAndResetPassword_AllowedWithoutToken()
    {
        // Act 1: POST a /identidad/api/v1/auth/forgot-password sin encabezado Authorization
        var forgotResponse = await _gatewayClient.PostAsJsonAsync("/identidad/api/v1/auth/forgot-password", new
        {
            email = "usuario.public@protraining.app"
        });

        // Assert 1: Debe atravesar el Gateway hacia Identidad respondiendo 202 Accepted (no 401 ni 404)
        Assert.Equal(HttpStatusCode.Accepted, forgotResponse.StatusCode);

        // Act 2: POST a /identidad/api/v1/auth/reset-password con token inválido sin Authorization
        var resetResponse = await _gatewayClient.PostAsJsonAsync("/identidad/api/v1/auth/reset-password", new
        {
            email = "usuario.public@protraining.app",
            token = "token_invalido",
            newPassword = "PasswordNueva123"
        });

        // Assert 2: Atraviesa el Gateway respondiendo 400 Bad Request desde Identidad (no 401 de autenticación de Gateway)
        Assert.Equal(HttpStatusCode.BadRequest, resetResponse.StatusCode);

        // Act 3: POST a /identidad/api/v1/auth/google sin encabezado Authorization (#KAN-541)
        var googleResponse = await _gatewayClient.PostAsJsonAsync("/identidad/api/v1/auth/google", new
        {
            idToken = "invalid_token_perimeter"
        });

        // Assert 3: Atraviesa el Gateway respondiendo 401 Unauthorized desde el controlador de Identidad (no 401 por falta de Bearer en Gateway)
        Assert.Equal(HttpStatusCode.Unauthorized, googleResponse.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        _gatewayFactory.Dispose();
        _gatewayClient.Dispose();
        await _identidadFactory.DisposeAsync();
    }
}

public sealed class GatewayTestFactory : WebApplicationFactory<Program>
{
    private readonly IdentidadTestApplicationFactory _identidadFactory;
    public CallCountingHandler CountingHandler { get; }

    public GatewayTestFactory(IdentidadTestApplicationFactory identidadFactory)
    {
        _identidadFactory = identidadFactory;
        CountingHandler = new CallCountingHandler(_identidadFactory.Server.CreateHandler());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "clave_local_de_desarrollo_cambiar_32+",
                ["Jwt:Issuer"] = "protraining-identidad",
                ["Jwt:Audience"] = "protraining",
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
                ["Services:Identidad:BaseUrl"] = "http://localhost/"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.AddHttpClient<ISessionValidationClient, SessionValidationClient>(client =>
            {
                client.BaseAddress = new Uri("http://localhost/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => CountingHandler);

            services.AddSingleton<IForwarderHttpClientFactory>(new TestForwarderHttpClientFactory(_identidadFactory.Server.CreateHandler()));
        });
    }
}

public sealed class CallCountingHandler : DelegatingHandler
{
    private int _calls;
    public int ValidateSessionCalls => _calls;

    public CallCountingHandler(HttpMessageHandler inner) : base(inner)
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath.Contains("validate-session", StringComparison.OrdinalIgnoreCase) == true)
        {
            Interlocked.Increment(ref _calls);
        }
        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class TestForwarderHttpClientFactory : IForwarderHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public TestForwarderHttpClientFactory(HttpMessageHandler handler)
    {
        _handler = handler;
    }

    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
    {
        return new HttpMessageInvoker(_handler);
    }
}

public sealed class GatewayFailingBackendFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "clave_local_de_desarrollo_cambiar_32+",
                ["Jwt:Issuer"] = "protraining-identidad",
                ["Jwt:Audience"] = "protraining",
                ["Services:Identidad:BaseUrl"] = "http://localhost:59999" // Puerto inalcanzable para forzar fallo
            });
        });

        builder.ConfigureServices(services =>
        {
            services.AddHttpClient<ISessionValidationClient, SessionValidationClient>(client =>
            {
                client.BaseAddress = new Uri("http://localhost:59999");
                client.Timeout = TimeSpan.FromMilliseconds(500);
            });
        });
    }
}
