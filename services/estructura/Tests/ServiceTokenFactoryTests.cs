using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Estructura.Tests;

public class ServiceTokenFactoryTests
{
    private const string ValidKey = "clave_secreta_compartida_para_servicios_32+";

    private static IConfiguration CreateConfig(string? key = ValidKey, string? issuer = "protraining-estructura", string? audience = "protraining")
    {
        var dict = new Dictionary<string, string?>();
        if (key is not null) dict["Jwt:Key"] = key;
        if (issuer is not null) dict["Jwt:Issuer"] = issuer;
        if (audience is not null) dict["Jwt:Audience"] = audience;

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void Given_ValidConfiguration_When_CreateToken_Then_GeneratesJwtWithServiceRoleAndFiveMinutesExpiry()
    {
        // arrange
        var config = CreateConfig();
        var factory = new ServiceTokenFactory(config);

        // act
        var tokenString = factory.CreateToken();

        // assert
        Assert.NotNull(tokenString);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        Assert.Equal("protraining-estructura", jwt.Issuer);
        Assert.Contains("protraining", jwt.Audiences);

        var roleClaim = jwt.Claims.FirstOrDefault(c => c.Type == "role");
        Assert.NotNull(roleClaim);
        Assert.Equal("Servicio", roleClaim.Value);

        var subClaim = jwt.Claims.FirstOrDefault(c => c.Type == "sub");
        Assert.NotNull(subClaim);
        Assert.Equal("estructura", subClaim.Value);

        // Vigencia: debe expirar aproximadamente en 5 minutos
        var diff = jwt.ValidTo - DateTime.UtcNow;
        Assert.True(diff.TotalMinutes is >= 4 and <= 6, $"La vigencia calculada fue {diff.TotalMinutes} minutos.");
    }

    [Fact]
    public void Given_MissingOrShortKey_When_CreateToken_Then_ThrowsInvalidOperationException()
    {
        // arrange: clave corta menor a 32 caracteres
        var factoryShortKey = new ServiceTokenFactory(CreateConfig(key: "clave_corta"));
        var factoryMissingKey = new ServiceTokenFactory(CreateConfig(key: null));

        // act & assert
        Assert.Throws<InvalidOperationException>(() => factoryShortKey.CreateToken());
        Assert.Throws<InvalidOperationException>(() => factoryMissingKey.CreateToken());
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task Given_NoUserInContext_When_SendAsync_Then_InjectsServiceTokenAsBearer()
    {
        // arrange: proceso en segundo plano (worker) sin HttpContext de usuario
        var config = CreateConfig();
        var factory = new ServiceTokenFactory(config);
        var httpAccessor = new HttpContextAccessor();

        var capture = new CaptureHandler();
        var handler = new ServiceAuthenticationHandler(factory, httpAccessor)
        {
            InnerHandler = capture
        };

        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        // act
        await client.GetAsync("api/test");

        // assert: inyecta Bearer con el JWT del rol Servicio
        Assert.NotNull(capture.Request?.Headers.Authorization);
        Assert.Equal("Bearer", capture.Request!.Headers.Authorization!.Scheme);
        Assert.False(string.IsNullOrWhiteSpace(capture.Request.Headers.Authorization.Parameter));
        Assert.False(capture.Request.Headers.Contains("X-Service-Key"));
    }

    [Fact]
    public async Task Given_UserTokenInContext_When_SendAsync_Then_ForwardsUserAuthorization()
    {
        // arrange: peticion originada por un usuario
        var config = CreateConfig();
        var factory = new ServiceTokenFactory(config);
        var httpAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        httpAccessor.HttpContext.Request.Headers.Authorization = "Bearer token-del-usuario-123";

        var capture = new CaptureHandler();
        var handler = new ServiceAuthenticationHandler(factory, httpAccessor)
        {
            InnerHandler = capture
        };

        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        // act
        await client.GetAsync("api/test");

        // assert: reenvia el token del usuario sin generar token de servicio
        Assert.NotNull(capture.Request?.Headers.Authorization);
        Assert.Equal("Bearer token-del-usuario-123", capture.Request!.Headers.Authorization!.ToString());
    }
}
