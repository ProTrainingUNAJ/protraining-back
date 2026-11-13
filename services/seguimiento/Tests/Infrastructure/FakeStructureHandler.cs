using System.Net;
using System.Text;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Seguimiento.Tests.Infrastructure;

// Estructura falsa para los tests del semáforo: cada test decide qué le pasa al llamado.
// Por defecto la deja apagada (conexión rechazada), que es el escenario base de resiliencia (KAN-367).
// Las rejecciones del pipeline de Polly se simulan acá para que los tests sean determinísticos y
// rápidos, sin depender del AddStandardResilienceHandler real (que reintenta con backoff de segundos).
public sealed class FakeStructureHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Behavior { get; set; }
        = (_, _) => throw new HttpRequestException("Connection refused.");

    public HttpRequestMessage? LastRequest { get; private set; }

    // Estructura apagada: el transporte falla como una conexión rechazada.
    public void SimulateDown() =>
        Behavior = (_, _) => throw new HttpRequestException("Connection refused.");

    // El pipeline de resiliencia rechazó la ejecución por timeout (Polly v8).
    public void SimulateTimeoutRejection() =>
        Behavior = (_, _) => throw new TimeoutRejectedException("Se superó el timeout de la petición.");

    // El circuit breaker del pipeline está abierto: falla antes de salir de la casa (Polly v8).
    public void SimulateOpenCircuit() =>
        Behavior = (_, _) => throw new BrokenCircuitException("Circuito abierto contra Estructura.");

    // Estructura viva: responde la lista de atletas de la categoría.
    public void RespondWithAthletes(string athletesJson) =>
        Behavior = (_, _) => Task.FromResult(Json(HttpStatusCode.OK, athletesJson));

    // Estructura viva: cualquier atleta consultado es del PF (GET athletes/{id} → 200).
    // Lo usan los tests de lesiones que no prueban el aislamiento (KAN-431).
    public void RespondAllAthletesAreOwn() =>
        Behavior = (_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{}"));

    // Estructura viva pero con un error puntual (p. ej. 404 de categoría inexistente).
    public void RespondWith(HttpStatusCode status, string body) =>
        Behavior = (_, _) => Task.FromResult(Json(status, body));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return await Behavior(request, cancellationToken);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
