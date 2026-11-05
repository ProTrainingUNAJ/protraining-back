using System.Net;
using System.Text;

namespace Seguimiento.Tests.Infrastructure;

public sealed class FakeTrainingHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Behavior { get; set; }
        = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        });

    public HttpRequestMessage? LastRequest { get; private set; }

    public void RespondWith(HttpStatusCode status, string body) =>
        Behavior = (_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

    public void SimulateDown() =>
        Behavior = (_, _) => throw new HttpRequestException("Connection refused.");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return await Behavior(request, cancellationToken);
    }
}

