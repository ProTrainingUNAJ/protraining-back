using System.Net;
using API.Services;
using Application.Interfaces.Services;
using Moq;
using Xunit;

namespace Asistente.Tests;

// KAN-578: los endpoints internos de Estructura exigen rol 'Servicio'; el token del PF no sirve
public sealed class ServiceAuthorizationHandlerTests
{
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task SendAsync_ReplacesUserTokenWithServiceToken()
    {
        var tokenFactory = new Mock<IServiceTokenFactory>();
        tokenFactory.Setup(f => f.CreateToken()).Returns("token-de-servicio");
        var capture = new CaptureHandler();
        using var client = new HttpClient(new ServiceAuthorizationHandler(tokenFactory.Object) { InnerHandler = capture });

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://estructura/api/v1/internal/categories/x/events");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer token-del-pf");
        await client.SendAsync(request);

        Assert.Equal("Bearer", capture.LastRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("token-de-servicio", capture.LastRequest.Headers.Authorization?.Parameter);
    }
}
