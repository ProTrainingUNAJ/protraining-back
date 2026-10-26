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

public class SessionScheduleServiceTests
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

    private static SessionScheduleService CreateService(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://entrenamiento:8080/") }, NullLogger<SessionScheduleService>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Given_AthletesWithSession_When_GetAthletesWithScheduledSession_Then_Returns_Ids()
    {
        // arrange
        var athleteId1 = Guid.NewGuid();
        var athleteId2 = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 20);

        var handler = new FakeHttpMessageHandler(_ => Json(HttpStatusCode.OK,
            $$"""["{{athleteId1}}", "{{athleteId2}}"]"""));
        var service = CreateService(handler);

        // act
        var result = await service.GetAthletesWithScheduledSessionAsync(date, pfId);

        // assert
        Assert.Equal(2, result.Count);
        Assert.Contains(athleteId1, result);
        Assert.Contains(athleteId2, result);
        Assert.Equal($"http://entrenamiento:8080/api/v1/internal/training-sessions/athletes-with-session?date=2026-10-20&pfId={pfId}", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Given_NoSessionsFound_When_GetAthletesWithScheduledSession_Then_Returns_EmptyList()
    {
        var handler = new FakeHttpMessageHandler(_ => Json(HttpStatusCode.NotFound, "[]"));
        var service = CreateService(handler);

        var result = await service.GetAthletesWithScheduledSessionAsync(new DateOnly(2026, 10, 20));

        Assert.Empty(result);
    }

    [Fact]
    public async Task Given_EntrenamientoDownOrTimeout_When_GetAthletesWithScheduledSession_Then_DoesNotThrow_AndReturns_Empty()
    {
        // Criterio de aceptacion: si Entrenamiento se cae o no responde, el worker no se interrumpe
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("Connection refused"));
        var service = CreateService(handler);

        var result = await service.GetAthletesWithScheduledSessionAsync(new DateOnly(2026, 10, 20));

        Assert.Empty(result);
    }
}

