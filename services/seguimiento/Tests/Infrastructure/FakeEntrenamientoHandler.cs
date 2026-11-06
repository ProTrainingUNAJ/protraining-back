using System.Net;
using System.Text;
using System.Text.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Seguimiento.Tests.Infrastructure;

// Entrenamiento falso para los tests de confirmación de KAN-426: cada test decide qué
// le pasa a las dos llamadas del cliente (búsqueda de ejercicio y guardado del récord).
// Por defecto responde todo OK. Las rejecciones del pipeline de Polly se simulan acá
// para que los tests sean determinísticos y rápidos, sin depender del
// AddStandardResilienceHandler real (que reintenta con backoff de segundos).
public sealed class FakeEntrenamientoHandler : HttpMessageHandler
{
    // Ejercicios que Entrenamiento "tiene" por nombre (id fijo por nombre para el test).
    private readonly Dictionary<string, Guid> _exercises = new(StringComparer.OrdinalIgnoreCase);

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Behavior { get; set; }
        = (_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{}"));

    public HttpRequestMessage? LastRequest { get; private set; }
    public int ExerciseSearchCalls { get; private set; }
    public int SaveRecordCalls { get; private set; }
    public List<(Guid AthleteId, Guid ExerciseId, decimal Weight, string Origin)> SavedRecords { get; } = [];

    // Entrenamiento con un ejercicio de nombre exacto (el id lo elige el test).
    public void WithExercise(string name, Guid exerciseId) => _exercises[name] = exerciseId;

    // Entrenamiento vivo: responde la búsqueda de ejercicio y guarda el récord.
    public void RespondOk()
    {
        ExerciseSearchCalls = 0;
        SaveRecordCalls = 0;
        SavedRecords.Clear();
        Behavior = async (request, _) =>
        {
            if (IsExerciseSearch(request))
            {
                ExerciseSearchCalls++;
                return await Task.FromResult(Json(HttpStatusCode.OK, ExercisesJson()));
            }

            if (IsSaveRecord(request))
            {
                SaveRecordCalls++;
                var body = await ReadBodyAsync(request);
                SavedRecords.Add(ParseRecord(request, body));
                return Json(HttpStatusCode.OK, "{}");
            }

            return Json(HttpStatusCode.NotFound, "{}");
        };
    }

    // Entrenamiento apagado: el transporte falla como una conexión rechazada.
    public void SimulateDown() =>
        Behavior = (_, _) => throw new HttpRequestException("Connection refused.");

    // El pipeline de resiliencia rechazó la ejecución por timeout (Polly v8).
    public void SimulateTimeoutRejection() =>
        Behavior = (_, _) => throw new TimeoutRejectedException("Se superó el timeout de la petición.");

    // El circuit breaker del pipeline está abierto: falla antes de salir de la casa (Polly v8).
    public void SimulateOpenCircuit() =>
        Behavior = (_, _) => throw new BrokenCircuitException("Circuito abierto contra Entrenamiento.");

    // Entrenamiento rechaza el récord (p. ej. peso fuera de rango).
    public void RespondWithBadRequest() =>
        Behavior = (_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest,
            """{"title":"El peso debe estar entre 1 y 500 kg."}"""));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return await Behavior(request, cancellationToken);
    }

    private static bool IsExerciseSearch(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get &&
        request.RequestUri!.AbsolutePath.EndsWith("/exercises", StringComparison.Ordinal);

    private static bool IsSaveRecord(HttpRequestMessage request) =>
        request.Method == HttpMethod.Post &&
        request.RequestUri!.AbsolutePath.Contains("/records", StringComparison.Ordinal);

    private static async Task<string> ReadBodyAsync(HttpRequestMessage request) =>
        request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync();

    // POST /api/v1/athletes/{id}/records → (athleteId, exerciseId, weight, origin).
    private static (Guid, Guid, decimal, string) ParseRecord(HttpRequestMessage request, string body)
    {
        var athleteId = Guid.Parse(request.RequestUri!.AbsolutePath.Split('/')[^2]);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var exerciseId = root.GetProperty("exerciseId").GetGuid();
        var weight = root.GetProperty("weightOneRepMaxKg").GetDecimal();
        var origin = root.GetProperty("origin").GetString() ?? string.Empty;
        return (athleteId, exerciseId, weight, origin);
    }

    // Respuesta paginada como la de GET /api/v1/exercises (PaginatedExerciseResponse).
    private string ExercisesJson()
    {
        var items = _exercises.Select(pair => new
        {
            id = pair.Value,
            name = pair.Key
        });

        return JsonSerializer.Serialize(new
        {
            items,
            page = 1,
            pageSize = 50,
            totalCount = _exercises.Count,
            totalPages = 1
        });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
