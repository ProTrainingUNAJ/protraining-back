using System.Net;
using System.Text;
using API.Services;
using Application.Exceptions;
using Infrastructure.Services.Structure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Seguimiento.Tests;

public class StructureClientTests
{
    private sealed class FakeStructureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private static StructureClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://estructura:8080/") }, NullLogger<StructureClient>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Given_AthleteLinked_When_GetMe_Then_Returns_The_AthleteId()
    {
        var athleteId = Guid.NewGuid();
        var handler = new FakeStructureHandler(_ => Json(HttpStatusCode.OK,
            $$"""{"athleteId":"{{athleteId}}","firstName":"Ana","lastName":"Pérez","fullName":"Ana Pérez","active":true,"categories":[]}"""));
        var client = CreateClient(handler);

        var me = await client.GetMeAsync();

        Assert.NotNull(me);
        Assert.Equal(athleteId, me.AthleteId);
        Assert.Equal("http://estructura:8080/api/v1/me", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Given_AccountWithoutAthlete_When_GetMe_Then_Returns_Null()
    {
        var client = CreateClient(new FakeStructureHandler(_ => Json(HttpStatusCode.NotFound, """{"title":"Atleta no encontrado."}""")));

        Assert.Null(await client.GetMeAsync());
    }

    [Fact]
    public async Task Given_TokenRejected_When_GetMe_Then_Throws_Unauthorized()
    {
        var client = CreateClient(new FakeStructureHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.GetMeAsync());
    }

    [Fact]
    public async Task Given_AccountWithoutAthleteRole_When_GetMe_Then_Throws_Forbidden()
    {
        var client = CreateClient(new FakeStructureHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));

        await Assert.ThrowsAsync<ForbiddenException>(() => client.GetMeAsync());
    }

    [Fact]
    public async Task Given_StructureFailing_When_GetMe_Then_Throws_StructureUnavailable()
    {
        var client = CreateClient(new FakeStructureHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        await Assert.ThrowsAsync<StructureUnavailableException>(() => client.GetMeAsync());
    }

    [Fact]
    public async Task Given_StructureUnreachable_When_GetMe_Then_Throws_StructureUnavailable()
    {
        var client = CreateClient(new FakeStructureHandler(_ => throw new HttpRequestException("Connection refused")));

        await Assert.ThrowsAsync<StructureUnavailableException>(() => client.GetMeAsync());
    }

    [Fact]
    public async Task Given_RequestWithToken_When_CallingStructure_Then_Forwards_The_Same_Authorization_Header()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer token-del-atleta";
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);

        var structure = new FakeStructureHandler(_ => Json(HttpStatusCode.OK, "{}"));
        var forward = new ForwardAuthorizationHandler(accessor.Object) { InnerHandler = structure };
        var client = CreateClient(forward);

        await client.GetMeAsync();

        Assert.Equal("Bearer token-del-atleta", structure.LastRequest!.Headers.Authorization!.ToString());
    }

    // ---------- GetCategoryAthletesAsync (semáforo por categoría, KAN-364) ----------

    [Fact]
    public async Task Given_CategoryWithAthletes_When_GetCategoryAthletes_Then_Returns_IdAndFullName()
    {
        var athleteId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        // Estructura devuelve más campos (email, teléfono, etc.): el DTO nuevo solo usa Id y FullName.
        var handler = new FakeStructureHandler(_ => Json(HttpStatusCode.OK,
            $$"""[{"id":"{{athleteId}}","firstName":"Lucas","lastName":"Fernández","fullName":"Lucas Fernández","email":"lucas@example.com","phone":"1234","active":true,"allowsHabitsRecommendations":true}]"""));
        var client = CreateClient(handler);

        var athletes = await client.GetCategoryAthletesAsync(categoryId);

        var athlete = Assert.Single(athletes);
        Assert.Equal(athleteId, athlete.Id);
        Assert.Equal("Lucas Fernández", athlete.FullName);
        Assert.Equal($"http://estructura:8080/api/v1/categories/{categoryId}/athletes",
            handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Given_UnknownCategory_When_GetCategoryAthletes_Then_Throws_CategoryNotFound()
    {
        var client = CreateClient(new FakeStructureHandler(_ => Json(HttpStatusCode.NotFound, """{"title":"Categoría no encontrada."}""")));

        await Assert.ThrowsAsync<CategoryNotFoundException>(() => client.GetCategoryAthletesAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Given_TokenRejected_When_GetCategoryAthletes_Then_Throws_Unauthorized()
    {
        var client = CreateClient(new FakeStructureHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.GetCategoryAthletesAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Given_AccountWithoutPfRole_When_GetCategoryAthletes_Then_Throws_Forbidden()
    {
        var client = CreateClient(new FakeStructureHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));

        await Assert.ThrowsAsync<ForbiddenException>(() => client.GetCategoryAthletesAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Given_StructureFailing_When_GetCategoryAthletes_Then_Throws_StructureUnavailable()
    {
        var client = CreateClient(new FakeStructureHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        await Assert.ThrowsAsync<StructureUnavailableException>(() => client.GetCategoryAthletesAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Given_StructureUnreachable_When_GetCategoryAthletes_Then_Throws_StructureUnavailable()
    {
        var client = CreateClient(new FakeStructureHandler(_ => throw new HttpRequestException("Connection refused")));

        await Assert.ThrowsAsync<StructureUnavailableException>(() => client.GetCategoryAthletesAsync(Guid.NewGuid()));
    }

    // ---------- Rejecciones del pipeline de resiliencia (KAN-367) ----------
    // El pipeline de Polly (AddStandardResilienceHandler en Program.cs) puede fallar ANTES de
    // llegar a la red: breaker abierto o timeout rechazado. Esas excepciones deben degradar a
    // StructureUnavailableException (el caso de uso responde 200), nunca escapar como 500.

    [Fact]
    public async Task Given_OpenCircuitBreaker_When_GetMe_Then_Throws_StructureUnavailable()
    {
        var client = CreateClient(new FakeStructureHandler(_ => throw new BrokenCircuitException()));

        await Assert.ThrowsAsync<StructureUnavailableException>(() => client.GetMeAsync());
    }

    [Fact]
    public async Task Given_RejectedTimeout_When_GetMe_Then_Throws_StructureUnavailable()
    {
        var client = CreateClient(new FakeStructureHandler(_ => throw new TimeoutRejectedException()));

        await Assert.ThrowsAsync<StructureUnavailableException>(() => client.GetMeAsync());
    }

    [Fact]
    public async Task Given_OpenCircuitBreaker_When_GetCategoryAthletes_Then_Throws_StructureUnavailable()
    {
        var client = CreateClient(new FakeStructureHandler(_ => throw new BrokenCircuitException()));

        await Assert.ThrowsAsync<StructureUnavailableException>(() => client.GetCategoryAthletesAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Given_RejectedTimeout_When_GetCategoryAthletes_Then_Throws_StructureUnavailable()
    {
        var client = CreateClient(new FakeStructureHandler(_ => throw new TimeoutRejectedException()));

        await Assert.ThrowsAsync<StructureUnavailableException>(() => client.GetCategoryAthletesAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Given_CallerCancelsTheRequest_When_GetMe_Then_PropagatesTheCancellation()
    {
        // El catch ampliado (KAN-367) no debe tragarse la cancelación real del caller.
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = CreateClient(new FakeStructureHandler(_ => throw new TaskCanceledException()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetMeAsync(cancellation.Token));
    }
}