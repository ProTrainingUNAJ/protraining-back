using System.Net;
using Application.Exceptions;
using Infrastructure.Services.Seguimiento;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Asistente.Tests;

public sealed class SeguimientoClientTests
{
    private sealed class TestHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetAthleteCheckInsHistoryAsync_WhenAuthorizationFails_ThrowsInsteadOfReturningEmpty(
        HttpStatusCode statusCode)
    {
        var client = CreateClient(new TestHandler(_ => new HttpResponseMessage(statusCode)));

        var action = () => client.GetAthleteCheckInsHistoryAsync(
            [Guid.NewGuid()],
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 7));

        if (statusCode == HttpStatusCode.Unauthorized)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(action);
        }
        else
        {
            await Assert.ThrowsAsync<SeguimientoServiceUnavailableException>(action);
        }
    }

    [Fact]
    public async Task GetAthleteCheckInsHistoryAsync_WhenServiceFails_ThrowsInsteadOfReturningEmpty()
    {
        var client = CreateClient(new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var action = () => client.GetAthleteCheckInsHistoryAsync(
            [Guid.NewGuid()],
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 7));

        await Assert.ThrowsAsync<SeguimientoServiceUnavailableException>(action);
    }

    [Fact]
    public async Task GetAthleteCheckInsHistoryAsync_WhenNotFound_ReturnsEmpty()
    {
        var client = CreateClient(new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await client.GetAthleteCheckInsHistoryAsync(
            [Guid.NewGuid()],
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 7));

        Assert.Empty(result);
    }

    private static SeguimientoClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://seguimiento:8080/")
        };

        return new SeguimientoClient(httpClient, NullLogger<SeguimientoClient>.Instance);
    }
}
