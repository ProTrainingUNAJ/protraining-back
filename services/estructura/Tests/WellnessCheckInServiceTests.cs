using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Estructura.Tests;

public class WellnessCheckInServiceTests
{
    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private static WellnessCheckInService CreateService(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://seguimiento:8080/") }, NullLogger<WellnessCheckInService>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Given_CheckInCompletedToday_When_HasCompletedCheckIn_Then_Returns_True()
    {
        // arrange
        var athleteId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 20);

        var handler = new FakeHttpMessageHandler(_ => Json(HttpStatusCode.OK,
            $$"""[{"date":"{{date:yyyy-MM-dd}}"}]"""));
        var service = CreateService(handler);

        // act
        var result = await service.HasCompletedCheckInAsync(athleteId, date);

        // assert
        Assert.True(result);
        Assert.Equal($"http://seguimiento:8080/api/v1/athletes/{athleteId}/check-ins?date=2026-10-20&limit=1", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Given_NoCheckInToday_When_HasCompletedCheckIn_Then_Returns_False()
    {
        var athleteId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 20);

        var handler = new FakeHttpMessageHandler(_ => Json(HttpStatusCode.OK, "[]"));
        var service = CreateService(handler);

        var result = await service.HasCompletedCheckInAsync(athleteId, date);

        Assert.False(result);
    }

    [Fact]
    public async Task Given_SeguimientoNotFound_When_HasCompletedCheckIn_Then_Returns_False()
    {
        var athleteId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = CreateService(handler);

        var result = await service.HasCompletedCheckInAsync(athleteId, new DateOnly(2026, 10, 20));

        Assert.False(result);
    }

    [Fact]
    public async Task Given_SeguimientoDownOrTimeout_When_HasCompletedCheckIn_Then_DoesNotThrow_AndReturns_False()
    {
        // criterio de aceptacion: si Seguimiento se cae o no responde, el worker no se interrumpe
        var athleteId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("Seguimiento timeout"));
        var service = CreateService(handler);

        var result = await service.HasCompletedCheckInAsync(athleteId, new DateOnly(2026, 10, 20));

        Assert.False(result);
    }
}
