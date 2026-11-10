using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.DTOs.Auth;
using Domain.Entities;
using Gateway.Services;
using Identidad.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

    public GatewayTestFactory(IdentidadTestApplicationFactory identidadFactory)
    {
        _identidadFactory = identidadFactory;
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
            .ConfigurePrimaryHttpMessageHandler(() => _identidadFactory.Server.CreateHandler());

            services.AddSingleton<IForwarderHttpClientFactory>(new TestForwarderHttpClientFactory(_identidadFactory.Server.CreateHandler()));
        });
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
