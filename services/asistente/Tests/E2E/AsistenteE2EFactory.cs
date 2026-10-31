using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace Asistente.Tests.E2E;

// KAN-414: levanta el Asistente con su arranque real (Program.cs: migraciones, JWT, middlewares y
// controllers) sobre PostgreSQL real con Testcontainers. Solo se reemplazan tres cosas de afuera:
// la IA (siempre devuelve el mismo borrador, sin Ollama ni Gemini) y Entrenamiento y Estructura,
// que son servidores HTTP falsos que registran lo que reciben.
public sealed class AsistenteE2EFactory : WebApplicationFactory<Program>
{
    public const string JwtKey = "clave_de_prueba_e2e_asistente_32_caracteres+";
    public const string JwtIssuer = "protraining-test";
    public const string JwtAudience = "protraining";

    private readonly PostgreSqlContainer _postgres;

    public FakeExternalServices External { get; } = new();

    private AsistenteE2EFactory(PostgreSqlContainer postgres)
    {
        _postgres = postgres;
    }

    public static async Task<AsistenteE2EFactory> CreateAsync()
    {
        var postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await postgres.StartAsync();
        return new AsistenteE2EFactory(postgres);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting y no ConfigureAppConfiguration: Program.cs lee la base y las URLs antes de Build().
        builder.UseSetting("Database:Host", _postgres.Hostname);
        builder.UseSetting("Database:Port", _postgres.GetMappedPublicPort(5432).ToString());
        builder.UseSetting("Database:Name", "postgres");
        builder.UseSetting("Database:User", "postgres");
        builder.UseSetting("Database:Password", "postgres");
        builder.UseSetting("Services:Entrenamiento:BaseUrl", "http://entrenamiento.test/");
        builder.UseSetting("Services:Estructura:BaseUrl", "http://estructura.test/");
        builder.UseSetting("Jwt:Key", JwtKey);
        builder.UseSetting("Jwt:Issuer", JwtIssuer);
        builder.UseSetting("Jwt:Audience", JwtAudience);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiService>();
            services.AddScoped<IAiService, FakeAiService>();

            // Todos los HttpClient (Entrenamiento y Estructura) salen por el router falso.
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
                    handlerBuilder.PrimaryHandler = new FakeExternalHandler(External)));
        });
    }

    public HttpClient CreatePfClient(Guid pfId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreatePfToken(pfId));
        return client;
    }

    public static string CreatePfToken(Guid pfId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, pfId.ToString()),
            new(JwtRegisteredClaimNames.Name, $"PF {pfId:N}"),
            new("role", "PreparadorFisico"),
            new("pf_id", pfId.ToString()),
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(JwtIssuer, JwtAudience, claims,
            expires: DateTime.UtcNow.AddHours(1), signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

// Lo que pasa "afuera" del Asistente: el catálogo de Entrenamiento, cómo responde al crear y asignar
// la planificación, y todo lo que el Asistente le mandó.
public sealed class FakeExternalServices
{
    public sealed record RecordedRequest(HttpMethod Method, string Host, string PathAndQuery, string? Authorization, string Body);

    public ConcurrentDictionary<Guid, string> Catalog { get; } = new();
    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    public Guid CreatedPlanId { get; } = Guid.NewGuid();
    public Guid AssignmentId { get; } = Guid.NewGuid();
    public HttpStatusCode CreatePlanStatus { get; set; } = HttpStatusCode.Created;

    public IReadOnlyList<RecordedRequest> To(string host, HttpMethod method, string path) =>
        Requests.Where(r => r.Host == host && r.Method == method && r.PathAndQuery.StartsWith(path, StringComparison.Ordinal)).ToList();
}

internal sealed class FakeExternalHandler(FakeExternalServices state) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var uri = request.RequestUri!;
        state.Requests.Enqueue(new FakeExternalServices.RecordedRequest(
            request.Method, uri.Host, uri.PathAndQuery, request.Headers.Authorization?.ToString(), body));

        return (uri.Host, request.Method.Method, uri.AbsolutePath) switch
        {
            // Entrenamiento: catálogo por id (?ids=, KAN-414) o la primera página.
            ("entrenamiento.test", "GET", "/api/v1/exercises") => Json(HttpStatusCode.OK, CatalogPage(uri)),
            ("entrenamiento.test", "POST", "/api/v1/training-plans") =>
                state.CreatePlanStatus == HttpStatusCode.Created
                    ? Json(HttpStatusCode.Created, $$"""{"id":"{{state.CreatedPlanId}}"}""")
                    : Json(state.CreatePlanStatus, """{"title":"Entrenamiento no disponible"}"""),
            ("entrenamiento.test", "POST", "/api/v1/training-plans/assignments") =>
                Json(HttpStatusCode.Created, $$"""{"assignmentId":"{{state.AssignmentId}}"}"""),
            // Estructura: sin partidos en el período.
            ("estructura.test", "GET", _) => Json(HttpStatusCode.OK, "[]"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private string CatalogPage(Uri uri)
    {
        var ids = System.Web.HttpUtility.ParseQueryString(uri.Query).GetValues("ids") ?? [];
        var items = (ids.Length > 0
                ? ids.Select(Guid.Parse).Where(state.Catalog.ContainsKey)
                : state.Catalog.Keys)
            .Select(id => $$"""{"id":"{{id}}","name":"{{state.Catalog[id]}}","muscleGroup":"Piernas","equipment":"barra con pesas"}""");
        var list = string.Join(",", items);
        return $$"""{"items":[{{list}}],"page":1,"pageSize":100,"totalCount":{{items.Count()}},"totalPages":1}""";
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

// La IA de prueba: siempre el mismo borrador (dos sesiones), sin llamar a Ollama ni a Gemini.
internal sealed class FakeAiService : IAiService
{
    public static readonly Guid SquatId = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    public static readonly Guid BenchId = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    public static readonly Guid RowId = Guid.Parse("a1000000-0000-0000-0000-000000000003");

    public Task<RoutineSuggestionResponseDto> SuggestRoutineAsync(
        RoutineSuggestionRequestDto request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("El test de punta a punta solo usa el borrador de planificación.");

    public Task<(PlanDraftContentDto Content, string Provider)> GeneratePlanDraftAsync(
        CreatePlanDraftRequestDto request, IReadOnlyList<DateOnly>? matchDates = null, CancellationToken cancellationToken = default)
    {
        var content = new PlanDraftContentDto(
            Name: "Fuerza base",
            Objective: request.Goal,
            JustificationSummary: "Borrador fijo de la IA de prueba (KAN-414).",
            Weeks:
            [
                new PlanDraftWeekDto(1,
                [
                    new PlanDraftSessionDto("Piernas", DayOfWeek.Monday, 1, null,
                        [new PlanDraftExerciseDto(SquatId, 1, 3, 8, 70m)]),
                    new PlanDraftSessionDto("Empuje y tracción", DayOfWeek.Wednesday, 2, null,
                        [new PlanDraftExerciseDto(BenchId, 1, 3, 8, 70m), new PlanDraftExerciseDto(RowId, 2, 3, 10, 65m)]),
                ]),
            ]);

        return Task.FromResult((content, "IaDePrueba"));
    }
}
