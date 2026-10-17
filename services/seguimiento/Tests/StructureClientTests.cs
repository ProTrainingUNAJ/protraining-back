using System.Net;
using System.Text;
using API.Services;
using Application.Exceptions;
using Infrastructure.Services.Structure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

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
}