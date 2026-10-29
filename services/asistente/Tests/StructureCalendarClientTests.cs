namespace Asistente.Tests;

using System.Net;
using System.Text;
using API.Services;
using Infrastructure.Services.Structure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Pruebas unitarias para el cliente HTTP resiliente de eventos de calendario de Estructura (#111 · KAN-406).
/// Valida el consumo de partidos, filtrado por fechas, reenvío de Authorization y degradación graciosa sin excepciones.
/// </summary>
public sealed class StructureCalendarClientTests
{
    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private static StructureCalendarClient CreateClient(
        HttpMessageHandler handler,
        ILogger<StructureCalendarClient>? logger = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://estructura:8080/")
        };
        return new StructureCalendarClient(httpClient, logger ?? NullLogger<StructureCalendarClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Given_CategoryWithMatchOnSaturday_When_GetMatchDates_Then_ReturnsMatchDates()
    {
        // Arrange: Categoría con partido el sábado 7 de Noviembre de 2026
        var categoryId = Guid.NewGuid();
        var from = new DateOnly(2026, 11, 2);
        var to = new DateOnly(2026, 11, 8);
        var matchDate = new DateOnly(2026, 11, 7);

        var json = $$"""
            [
                { "date": "{{matchDate:yyyy-MM-dd}}", "type": "Match" }
            ]
            """;

        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, json));
        var client = CreateClient(handler);

        // Act
        var result = await client.GetMatchDatesAsync(categoryId, from, to);

        // Assert
        Assert.NotNull(result);
        var singleMatch = Assert.Single(result);
        Assert.Equal(matchDate, singleMatch);
        Assert.Equal(DayOfWeek.Saturday, singleMatch.DayOfWeek);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        var expectedUri = $"http://estructura:8080/api/v1/categories/{categoryId}/events?from=2026-11-02&to=2026-11-08";
        Assert.Equal(expectedUri, handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task Given_MultipleAndDuplicateDates_When_GetMatchDates_Then_ReturnsDistinctOrderedDates()
    {
        // Arrange: Días desordenados y con duplicados
        var categoryId = Guid.NewGuid();
        var from = new DateOnly(2026, 11, 1);
        var to = new DateOnly(2026, 11, 30);

        var json = """
            [
                { "date": "2026-11-14", "type": "Match" },
                { "date": "2026-11-07", "type": "Match" },
                { "date": "2026-11-14", "type": "Match" },
                { "date": "2026-11-21", "type": "Match" }
            ]
            """;

        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, json));
        var client = CreateClient(handler);

        // Act
        var result = await client.GetMatchDatesAsync(categoryId, from, to);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(new DateOnly(2026, 11, 7), result[0]);
        Assert.Equal(new DateOnly(2026, 11, 14), result[1]);
        Assert.Equal(new DateOnly(2026, 11, 21), result[2]);
    }

    [Fact]
    public async Task Given_StructureReturns500InternalServerError_When_GetMatchDates_Then_ReturnsEmptyListAndLogsWarningWithoutThrowing()
    {
        // Arrange: Estructura falla con HTTP 500
        var categoryId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler);

        // Act: No debe arrojar excepción (criterio de aceptación #111)
        var result = await client.GetMatchDatesAsync(categoryId, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8));

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Given_StructureUnreachableConnectionRefused_When_GetMatchDates_Then_ReturnsEmptyListWithoutThrowing()
    {
        // Arrange: Fallo de red a nivel socket / conexión rechazada
        var categoryId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("Connection refused (estructura:8080)"));
        var client = CreateClient(handler);

        // Act & Assert
        var result = await client.GetMatchDatesAsync(categoryId, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8));

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Given_StructureReturnsEmptyListOrNull_When_GetMatchDates_Then_ReturnsEmptyList()
    {
        // Arrange: Respuesta JSON vacía
        var categoryId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, "[]"));
        var client = CreateClient(handler);

        // Act
        var result = await client.GetMatchDatesAsync(categoryId, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8));

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Given_EmptyCategoryId_When_GetMatchDates_Then_ReturnsEmptyListImmediatelyWithoutHttpCall()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, "[]"));
        var client = CreateClient(handler);

        // Act
        var result = await client.GetMatchDatesAsync(Guid.Empty, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8));

        // Assert
        Assert.Empty(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Given_ExternalCancellationRequested_When_GetMatchDates_Then_PropagatesOperationCanceledException()
    {
        // Arrange: Token cancelado antes o durante la petición por el cliente
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, "[]"));
        var client = CreateClient(handler);

        // Act & Assert: Debe propagar si la cancelación vino expresamente del llamador
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetMatchDatesAsync(Guid.NewGuid(), new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8), cts.Token));
    }

    [Fact]
    public async Task Given_RequestWithPfToken_When_CallingStructure_Then_ForwardsAuthorizationHeader()
    {
        // Arrange: Reenvío del token JWT mediante ForwardAuthorizationHandler
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer token-del-preparador-fisico";
        var accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.Setup(a => a.HttpContext).Returns(httpContext);

        var innerHandler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, "[]"));
        var forwardHandler = new ForwardAuthorizationHandler(accessorMock.Object)
        {
            InnerHandler = innerHandler
        };

        var client = CreateClient(forwardHandler);

        // Act
        await client.GetMatchDatesAsync(Guid.NewGuid(), new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8));

        // Assert: El header Authorization debe haber sido inyectado en el HttpRequestMessage
        Assert.NotNull(innerHandler.LastRequest);
        Assert.NotNull(innerHandler.LastRequest.Headers.Authorization);
        Assert.Equal("Bearer", innerHandler.LastRequest.Headers.Authorization.Scheme);
        Assert.Equal("token-del-preparador-fisico", innerHandler.LastRequest.Headers.Authorization.Parameter);
    }
}

