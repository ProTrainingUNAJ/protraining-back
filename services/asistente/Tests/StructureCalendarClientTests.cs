namespace Asistente.Tests;

using System.Net;
using System.Text;
using API.Services;
using Application.Exceptions;
using Infrastructure.Services.Structure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Pruebas unitarias para el cliente HTTP resiliente de eventos de calendario de Estructura (#111 · KAN-406 · #178 · KAN-473 · #191 · KAN-486).
/// Valida el consumo de partidos vía endpoint interno, reenvío de Authorization, degradación elegante a caché y lanzamiento de excepciones ante 401/403.
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
        ILogger<StructureCalendarClient>? logger = null,
        IMemoryCache? memoryCache = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://estructura:8080/")
        };
        return new StructureCalendarClient(httpClient, logger ?? NullLogger<StructureCalendarClient>.Instance, memoryCache);
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
                { "date": "{{matchDate:yyyy-MM-dd}}", "type": "Official" }
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
        var expectedUri = $"http://estructura:8080/api/v1/internal/categories/{categoryId}/events?from=2026-11-02&to=2026-11-08";
        Assert.Equal(expectedUri, handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task Given_StructureReturns403Forbidden_When_GetMatchDates_Then_ThrowsStructureServiceUnavailableException()
    {
        // Arrange: Estructura responde 403 Forbidden
        var categoryId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = CreateClient(handler);

        // Act & Assert (No debe silenciar errores de autorización #178 · KAN-473)
        await Assert.ThrowsAsync<StructureServiceUnavailableException>(() =>
            client.GetMatchDatesAsync(categoryId, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8)));
    }

    [Fact]
    public async Task Given_StructureReturns401Unauthorized_When_GetMatchDates_Then_ThrowsStructureServiceUnavailableException()
    {
        // Arrange: Estructura responde 401 Unauthorized
        var categoryId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = CreateClient(handler);

        // Act & Assert
        await Assert.ThrowsAsync<StructureServiceUnavailableException>(() =>
            client.GetMatchDatesAsync(categoryId, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8)));
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
                { "date": "2026-11-14", "type": "Official" },
                { "date": "2026-11-07", "type": "Official" },
                { "date": "2026-11-14", "type": "Official" },
                { "date": "2026-11-21", "type": "Official" }
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
    public async Task Given_StructureReturns500InternalServerError_When_GetMatchDates_Then_ThrowsStructureServiceUnavailableException()
    {
        // Arrange: Estructura falla con HTTP 500
        var categoryId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler);

        // Act & Assert: No se debe ocultar un fallo del servicio como ausencia de partidos.
        await Assert.ThrowsAsync<StructureServiceUnavailableException>(() =>
            client.GetMatchDatesAsync(categoryId, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8)));
    }

    [Fact]
    public async Task Given_StructureUnreachableConnectionRefused_When_GetMatchDates_Then_ThrowsStructureServiceUnavailableException()
    {
        // Arrange: Fallo de red a nivel socket / conexión rechazada
        var categoryId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("Connection refused (estructura:8080)"));
        var client = CreateClient(handler);

        // Act & Assert: Un servicio caído no equivale a un calendario vacío.
        await Assert.ThrowsAsync<StructureServiceUnavailableException>(() =>
            client.GetMatchDatesAsync(categoryId, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8)));
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

    [Fact]
    public async Task Given_SuccessfulFetch_Then_StructureFailsWith500_When_GetMatchDates_Then_DegradesGracefullyToCachedDates()
    {
        // Arrange: KAN-486 Degradación elegante a caché ante fallo de Estructura
        var categoryId = Guid.NewGuid();
        var from = new DateOnly(2026, 11, 2);
        var to = new DateOnly(2026, 11, 8);
        var matchDate = new DateOnly(2026, 11, 7);

        var json = $$"""
            [
                { "date": "{{matchDate:yyyy-MM-dd}}", "type": "Official" }
            ]
            """;

        var call = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            call++;
            return call == 1
                ? JsonResponse(HttpStatusCode.OK, json)
                : new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var cache = new MemoryCache(new MemoryCacheOptions());
        var client = CreateClient(handler, memoryCache: cache);

        // Act 1: Primera llamada exitosa, puebla la caché
        var firstResult = await client.GetMatchDatesAsync(categoryId, from, to);
        Assert.Single(firstResult);
        Assert.Equal(matchDate, firstResult[0]);

        // Act 2: Segunda llamada con Estructura caído (500) -> Debe degradar a caché sin arrojar excepción
        var fallbackResult = await client.GetMatchDatesAsync(categoryId, from, to);

        // Assert
        Assert.NotNull(fallbackResult);
        var date = Assert.Single(fallbackResult);
        Assert.Equal(matchDate, date);
    }

    [Fact]
    public async Task Given_SuccessfulFetch_Then_StructureThrowsConnectionRefused_When_GetMatchDates_Then_DegradesGracefullyToCachedDates()
    {
        // Arrange: KAN-486 Caída total por excepción de conectividad
        var categoryId = Guid.NewGuid();
        var from = new DateOnly(2026, 11, 2);
        var to = new DateOnly(2026, 11, 8);
        var matchDate = new DateOnly(2026, 11, 7);

        var json = $$"""
            [
                { "date": "{{matchDate:yyyy-MM-dd}}", "type": "Official" }
            ]
            """;

        var call = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            call++;
            if (call == 1)
                return JsonResponse(HttpStatusCode.OK, json);

            throw new HttpRequestException("Connection refused (estructura:8080)");
        });

        var cache = new MemoryCache(new MemoryCacheOptions());
        var client = CreateClient(handler, memoryCache: cache);

        // 1. Primer llamado exitoso
        await client.GetMatchDatesAsync(categoryId, from, to);

        // 2. Segundo llamado con error de red -> degrada a caché
        var fallbackResult = await client.GetMatchDatesAsync(categoryId, from, to);

        Assert.NotNull(fallbackResult);
        var date = Assert.Single(fallbackResult);
        Assert.Equal(matchDate, date);
    }

    [Fact]
    public async Task Given_CacheExists_When_StructureReturns403Forbidden_Then_DoesNotUseCacheAndThrowsException()
    {
        // Arrange: Un error 403 Forbidden no debe ser silenciado ni enmascarado con datos cacheados
        var categoryId = Guid.NewGuid();
        var from = new DateOnly(2026, 11, 2);
        var to = new DateOnly(2026, 11, 8);

        var json = """[ { "date": "2026-11-07", "type": "Official" } ]""";
        var call = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            call++;
            return call == 1
                ? JsonResponse(HttpStatusCode.OK, json)
                : new HttpResponseMessage(HttpStatusCode.Forbidden);
        });

        var cache = new MemoryCache(new MemoryCacheOptions());
        var client = CreateClient(handler, memoryCache: cache);

        // Puebla caché
        await client.GetMatchDatesAsync(categoryId, from, to);

        // Al recibir 403, debe lanzar StructureServiceUnavailableException
        await Assert.ThrowsAsync<StructureServiceUnavailableException>(() =>
            client.GetMatchDatesAsync(categoryId, from, to));
    }
}
