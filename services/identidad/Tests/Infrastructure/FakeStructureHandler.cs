using System.Net;
using System.Text;
using System.Text.Json;

namespace Identidad.Tests.Infrastructure;

public sealed class FakeStructureHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Behavior { get; set; }
        = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

    public HttpRequestMessage? LastRequest { get; private set; }

    public void RespondWithInvitation(string token, Guid categoryId, string categoryName, DateTime expiresAt)
    {
        var json = JsonSerializer.Serialize(new
        {
            token,
            categoryId,
            categoryName,
            expiresAt
        });
        Behavior = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }

    public void RespondWithExpired(string token)
    {
        Behavior = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Gone)
        {
            Content = new StringContent("{\"title\":\"Invitación expirada.\"}", Encoding.UTF8, "application/json")
        });
    }

    public void RespondWithNotFound()
    {
        Behavior = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"title\":\"Invitación no encontrada.\"}", Encoding.UTF8, "application/json")
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return await Behavior(request, cancellationToken);
    }
}