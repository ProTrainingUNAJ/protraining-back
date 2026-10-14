using System.Net;
using System.Text;
using API.Services;
using Application.Exceptions;
using Infrastructure.Services.Structure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Entrenamiento.Tests;

public class StructureClientTests
{
    // Simula a Estructura: devuelve la respuesta configurada y guarda el pedido que recibió.
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
    public async Task Given_AthleteWithCategories_When_GetMyCategories_Then_Returns_Them_From_Me_Endpoint()
    {
        // arrange
        var categoryId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var handler = new FakeStructureHandler(_ => Json(HttpStatusCode.OK,
            $$"""[{"id":"{{categoryId}}","organizationId":"{{organizationId}}","name":"Primera","description":null}]"""));
        var client = CreateClient(handler);

        // act
        var categories = await client.GetMyCategoriesAsync();

        // assert
        var category = Assert.Single(categories);
        Assert.Equal(categoryId, category.Id);
        Assert.Equal(organizationId, category.OrganizationId);
        Assert.Equal("Primera", category.Name);
        Assert.Equal("http://estructura:8080/api/v1/me/categories", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Given_AccountNotLinkedToAthlete_When_GetMyCategories_Then_Returns_Empty()
    {
        // arrange
        var client = CreateClient(new FakeStructureHandler(_ => Json(HttpStatusCode.NotFound, """{"title":"Atleta no encontrado."}""")));

        // act
        var categories = await client.GetMyCategoriesAsync();

        // assert
        Assert.Empty(categories);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Given_TokenRejectedByStructure_When_GetMyCategories_Then_Throws_Unauthorized(HttpStatusCode status)
    {
        // arrange
        var client = CreateClient(new FakeStructureHandler(_ => new HttpResponseMessage(status)));

        // act & assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.GetMyCategoriesAsync());
    }

    [Fact]
    public async Task Given_StructureFailing_When_GetMyCategories_Then_Throws_StructureUnavailable()
    {
        // arrange
        var client = CreateClient(new FakeStructureHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        // act & assert
        await Assert.ThrowsAsync<StructureUnavailableException>(() => client.GetMyCategoriesAsync());
    }

    [Fact]
    public async Task Given_StructureUnreachable_When_GetMyCategories_Then_Throws_StructureUnavailable()
    {
        // arrange
        var client = CreateClient(new FakeStructureHandler(_ => throw new HttpRequestException("Connection refused")));

        // act & assert
        await Assert.ThrowsAsync<StructureUnavailableException>(() => client.GetMyCategoriesAsync());
    }

    [Fact]
    public async Task Given_RequestWithToken_When_CallingStructure_Then_Forwards_The_Same_Authorization_Header()
    {
        // arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer token-del-atleta";
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);

        var structure = new FakeStructureHandler(_ => Json(HttpStatusCode.OK, "[]"));
        var forward = new ForwardAuthorizationHandler(accessor.Object) { InnerHandler = structure };
        var client = CreateClient(forward);

        // act
        await client.GetMyCategoriesAsync();

        // assert
        Assert.Equal("Bearer token-del-atleta", structure.LastRequest!.Headers.Authorization!.ToString());
    }
}
