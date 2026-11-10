using System.Net;
using Gateway.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Gateway.Tests;

public class InternalEndpointsBlockingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public InternalEndpointsBlockingTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "SuperSecretKeyThatIsAtLeast32BytesLong123456!",
                    ["Jwt:Issuer"] = "ProTraining",
                    ["Jwt:Audience"] = "ProTraining"
                });
            });

            builder.ConfigureServices(services =>
            {
                var mockSessionClient = new Mock<ISessionValidationClient>();
                services.AddSingleton(mockSessionClient.Object);
            });
        }).CreateClient();
    }

    [Theory]
    [InlineData("/entrenamiento/api/v1/internal/training-sessions/athletes-with-session")]
    [InlineData("/seguimiento/api/v1/internal/check-ins/done")]
    [InlineData("/identidad/api/v1/internal/auth/validate-session")]
    [InlineData("/estructura/api/v1/internal/some-resource")]
    [InlineData("/asistente/api/v1/internal/some-resource")]
    [InlineData("/api/v1/internal/check-ins/done")]
    public async Task Given_ExternalRequestToInternalEndpoint_When_SentToGateway_Then_Returns404NotFound(string url)
    {
        var responseGet = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, responseGet.StatusCode);

        var responsePost = await _client.PostAsync(url, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, responsePost.StatusCode);
    }

    [Fact]
    public async Task Given_HealthRequest_When_SentToGateway_Then_Returns200Ok()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

