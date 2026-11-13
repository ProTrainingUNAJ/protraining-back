using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.DTOs.FieldTests;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Seguimiento.Tests.Infrastructure;

namespace Seguimiento.Tests.Integration;

// Certificación HTTP: creación, listado por categoría y transición de estados
// de las sesiones de test de campo (Planned → InProgress → Closed, en orden estricto),
// el bloqueo de carga/corrección/confirmación en sesiones Closed (400) y el aislamiento
// por categoría de otro PF (404, sin escribir nada).
public sealed class FieldTestSessionManagementEndpointIntegrationTests
{
    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _otherPfId = Guid.NewGuid();

    // El contrato manda estados como texto: se serializa igual que en producción.
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private const string SessionsEndpoint = "api/v1/field-test-sessions";

    private static string SessionEndpoint(Guid sessionId) =>
        $"{SessionsEndpoint}/{sessionId}";

    private static string StatusEndpoint(Guid sessionId) =>
        $"{SessionsEndpoint}/{sessionId}/status";

    private static string ResultsEndpoint(Guid sessionId) =>
        $"{SessionsEndpoint}/{sessionId}/results";

    private static string ResultEndpoint(Guid sessionId, Guid resultId) =>
        $"{SessionsEndpoint}/{sessionId}/results/{resultId}";

    private static CreateFieldTestSessionRequest SesionValida(Guid categoryId, params Guid[] testIds) =>
        new()
        {
            CategoryId = categoryId,
            Date = new DateTime(2026, 11, 9),
            Name = "Evaluación de pretemporada",
            Tests = testIds
                .Select((testId, index) => new FieldTestSessionTestInput
                {
                    CatalogTestId = testId,
                    Order = index + 1
                })
                .ToList()
        };

    private static CreateFieldTestResultRequest ResultadoValido(Guid catalogTestId, decimal value = 38.5m) =>
        new()
        {
            CatalogTestId = catalogTestId,
            AthleteId = Guid.NewGuid(),
            Value = value,
            Attempt = 1
        };

    // Ids de dos tests del catálogo sembrado al arrancar el factory.
    private static Task<List<Guid>> ObtenerTestIdsAsync(SeguimientoTestApplicationFactory factory) =>
        factory.ExecuteInDbContextAsync(db =>
            db.CatalogTests.OrderBy(test => test.Name)
                .Select(test => test.Id)
                .Take(2)
                .ToListAsync());

    // ---------- POST: crear sesión ----------

    [Fact]
    public async Task Given_ValidRequest_When_PostSession_Then_Returns201PlannedAndPersistsWithTests()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);
        var testIds = await ObtenerTestIdsAsync(factory);
        var categoryId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            SessionsEndpoint, SesionValida(categoryId, testIds[0], testIds[1]), Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<FieldTestSessionDto>();
        Assert.NotNull(dto);
        Assert.Equal(categoryId, dto!.CategoryId);
        Assert.Equal("Evaluación de pretemporada", dto.Name);
        Assert.Equal("Planned", dto.Status);
        Assert.Equal(2, dto.Tests.Count);
        Assert.Empty(dto.Results);

        var saved = await factory.ExecuteInDbContextAsync(db =>
            db.FieldTestSessions.IgnoreQueryFilters().SingleAsync(session => session.Id == dto.Id));

        Assert.Equal(_pfId, saved.PfId);
        Assert.Equal(FieldTestSessionStatus.Planned, saved.Status);

        var savedTests = await factory.ExecuteInDbContextAsync(db =>
            db.FieldTestSessionTests.IgnoreQueryFilters()
                .Where(test => test.SessionId == dto.Id)
                .OrderBy(test => test.Order)
                .ToListAsync());

        Assert.Equal(2, savedTests.Count);
        Assert.Equal(testIds[0], savedTests[0].CatalogTestId);
        Assert.Equal(testIds[1], savedTests[1].CatalogTestId);
    }

    // Categoría inexistente o de otro PF → 404 y no se escribe nada.
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_ForeignCategory_When_PostSession_Then_Returns404AndPersistsNothing()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        // Estructura responde 404: la categoría no existe o es de otro PF.
        factory.Structure.RespondWith(HttpStatusCode.NotFound, "{}");
        var client = factory.CreateAuthenticatedClient(_pfId);
        var testIds = await ObtenerTestIdsAsync(factory);

        var response = await client.PostAsJsonAsync(
            SessionsEndpoint, SesionValida(Guid.NewGuid(), testIds[0]), Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("La categoría solicitada no existe.", problem!.Title);

        var persisted = await factory.ExecuteInDbContextAsync(db =>
            db.FieldTestSessions.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, persisted);
    }

    [Fact]
    public async Task Given_UnknownCatalogTest_When_PostSession_Then_Returns400AndPersistsNothing()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);
        var testIds = await ObtenerTestIdsAsync(factory);

        var response = await client.PostAsJsonAsync(
            SessionsEndpoint, SesionValida(Guid.NewGuid(), testIds[0], Guid.NewGuid()), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Una o más pruebas del catálogo no existen.", problem!.Title);

        var persisted = await factory.ExecuteInDbContextAsync(db =>
            db.FieldTestSessions.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, persisted);
    }

    // ---------- GET: listado por categoría ----------

    [Fact]
    public async Task Given_SessionsOfCategory_When_GetList_Then_Returns200WithSummaries()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);
        var testIds = await ObtenerTestIdsAsync(factory);
        var categoryId = Guid.NewGuid();

        var created = await client.PostAsJsonAsync(
            SessionsEndpoint, SesionValida(categoryId, testIds[0]), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var response = await client.GetAsync($"{SessionsEndpoint}?categoryId={categoryId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summaries = await response.Content.ReadFromJsonAsync<List<FieldTestSessionSummaryDto>>();
        Assert.NotNull(summaries);
        var summary = Assert.Single(summaries!);
        Assert.Equal(categoryId, summary.CategoryId);
        Assert.Equal("Evaluación de pretemporada", summary.Name);
        Assert.Equal("Planned", summary.Status);
    }

    [Fact]
    public async Task Given_WithoutCategoryId_When_GetList_Then_Returns400()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        var client = factory.CreateAuthenticatedClient(_pfId);

        var response = await client.GetAsync(SessionsEndpoint);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- PATCH: transición de estados ----------

    [Fact]
    public async Task Given_PlannedSession_When_PatchStatusInSequence_Then_Returns200AndPersists()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);
        var testIds = await ObtenerTestIdsAsync(factory);

        var created = await client.PostAsJsonAsync(
            SessionsEndpoint, SesionValida(Guid.NewGuid(), testIds[0]), Json);
        var session = await created.Content.ReadFromJsonAsync<FieldTestSessionDto>();

        var toInProgress = await client.PatchAsJsonAsync(
            StatusEndpoint(session!.Id),
            new UpdateFieldTestSessionStatusRequest { Status = FieldTestSessionStatus.InProgress },
            Json);

        Assert.Equal(HttpStatusCode.OK, toInProgress.StatusCode);
        var summary = await toInProgress.Content.ReadFromJsonAsync<FieldTestSessionSummaryDto>();
        Assert.Equal("InProgress", summary!.Status);

        var toClosed = await client.PatchAsJsonAsync(
            StatusEndpoint(session.Id),
            new UpdateFieldTestSessionStatusRequest { Status = FieldTestSessionStatus.Closed },
            Json);

        Assert.Equal(HttpStatusCode.OK, toClosed.StatusCode);

        var saved = await factory.ExecuteInDbContextAsync(db =>
            db.FieldTestSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == session.Id));
        Assert.Equal(FieldTestSessionStatus.Closed, saved.Status);
    }

    // Sin salto directo de Planned a Closed.
    [Fact]
    public async Task Given_PlannedSession_When_PatchStatusToClosed_Then_Returns400AndKeepsStatus()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);
        var testIds = await ObtenerTestIdsAsync(factory);

        var created = await client.PostAsJsonAsync(
            SessionsEndpoint, SesionValida(Guid.NewGuid(), testIds[0]), Json);
        var session = await created.Content.ReadFromJsonAsync<FieldTestSessionDto>();

        var response = await client.PatchAsJsonAsync(
            StatusEndpoint(session!.Id),
            new UpdateFieldTestSessionStatusRequest { Status = FieldTestSessionStatus.Closed },
            Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(
            "Transición de estado no permitida: de 'Planificada' a 'Cerrada'. Orden válido: 'Planificada' → 'En curso' → 'Cerrada'.",
            problem!.Title);

        var saved = await factory.ExecuteInDbContextAsync(db =>
            db.FieldTestSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == session.Id));
        Assert.Equal(FieldTestSessionStatus.Planned, saved.Status);
    }

    // ---------- Bloqueo de la sesión Closed (400 en POST/PUT/confirm) ----------

    [Fact]
    public async Task Given_ClosedSession_When_PostOrPutOrConfirmResult_Then_Returns400()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);
        var testIds = await ObtenerTestIdsAsync(factory);

        // Sesión propia que ya se cerró.
        var sessionId = await CrearSesionPropiaAsync(factory, FieldTestSessionStatus.Closed, testIds[0]);

        // POST de carga → 400 "La sesión ya está cerrada." y nada se escribe.
        var post = await client.PostAsJsonAsync(
            ResultsEndpoint(sessionId), ResultadoValido(testIds[0]), Json);

        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        var postProblem = await post.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("La sesión ya está cerrada.", postProblem!.Title);

        var persisted = await factory.ExecuteInDbContextAsync(db =>
            db.FieldTestResults.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, persisted);

        // PUT de corrección → 400 con el mismo mensaje.
        var put = await client.PutAsJsonAsync(
            ResultEndpoint(sessionId, Guid.NewGuid()),
            new UpdateFieldTestResultRequest { Value = 10.0m, Attempt = 1 },
            Json);

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        var putProblem = await put.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("La sesión ya está cerrada.", putProblem!.Title);

        // Confirm (envío del 1RM) → 400, sin disparar nada hacia Entrenamiento.
        var confirm = await client.PostAsync(
            ResultEndpoint(sessionId, Guid.NewGuid()) + "/confirm", null);

        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
        var confirmProblem = await confirm.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("La sesión ya está cerrada.", confirmProblem!.Title);
    }

    // Una sesión abierta sigue admitiendo carga: solo Closed bloquea.
    [Fact]
    public async Task Given_OpenSession_When_PostResult_Then_Returns201()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);
        var testIds = await ObtenerTestIdsAsync(factory);
        var sessionId = await CrearSesionPropiaAsync(factory, FieldTestSessionStatus.Planned, testIds[0]);

        var response = await client.PostAsJsonAsync(
            ResultsEndpoint(sessionId), ResultadoValido(testIds[0]), Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ---------- Aislamiento por categoría y sesión de otro PF ----------

    // El PATCH sobre la sesión de otro PF → 404 sin tocar la fila.
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_SessionOfAnotherPf_When_PatchStatus_Then_Returns404AndKeepsStatus()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        var client = factory.CreateAuthenticatedClient(_pfId);
        var foreignSessionId = await CrearSesionDeOtroPfAsync(factory);

        var response = await client.PatchAsJsonAsync(
            StatusEndpoint(foreignSessionId),
            new UpdateFieldTestSessionStatusRequest { Status = FieldTestSessionStatus.InProgress },
            Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var saved = await factory.ExecuteInDbContextAsync(db =>
            db.FieldTestSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == foreignSessionId));
        Assert.Equal(FieldTestSessionStatus.Planned, saved.Status);
        Assert.Equal(_otherPfId, saved.PfId);
    }

    // El listado de una categoría de otro PF no revela ninguna sesión.
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_CategoryOfAnotherPf_When_GetList_Then_Returns200Empty()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        var client = factory.CreateAuthenticatedClient(_pfId);

        // La sesión de otro PF vive en una categoría propia de ese PF.
        var foreignCategoryId = Guid.NewGuid();
        await CrearSesionDeOtroPfAsync(factory, foreignCategoryId);

        var response = await client.GetAsync($"{SessionsEndpoint}?categoryId={foreignCategoryId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summaries = await response.Content.ReadFromJsonAsync<List<FieldTestSessionSummaryDto>>();
        Assert.Empty(summaries!);
    }

    // ---------- Autorización (contrato: 401/403) ----------

    [Fact]
    public async Task Given_NoToken_When_PostSession_Then_Returns401()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        var testIds = await ObtenerTestIdsAsync(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(
            SessionsEndpoint, SesionValida(Guid.NewGuid(), testIds[0]), Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Given_AthleteRoleToken_When_PostSession_Then_Returns403()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        var testIds = await ObtenerTestIdsAsync(factory);

        var response = await factory
            .CreateAuthenticatedClient(_pfId, role: "Atleta")
            .PostAsJsonAsync(SessionsEndpoint, SesionValida(Guid.NewGuid(), testIds[0]), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- Helpers ----------

    // Sesión propia: se crea por EF para que AppDbContext le asigne el pf_id del token.
    // El test del catálogo se vincula a la sesión: el alta de resultados valida que el
    // resultado pertenezca a un test de esa sesión (CA2, KAN-568).
    private static async Task<Guid> CrearSesionPropiaAsync(
        SeguimientoTestApplicationFactory factory,
        FieldTestSessionStatus status,
        Guid catalogTestId)
    {
        var session = new FieldTestSession
        {
            CategoryId = Guid.NewGuid(),
            Date = new DateTime(2026, 11, 9),
            Name = "Evaluación de pretemporada",
            Status = status
        };

        await factory.ExecuteInDbContextAsync(async db =>
        {
            db.FieldTestSessions.Add(session);
            await db.SaveChangesAsync();

            db.FieldTestSessionTests.Add(new FieldTestSessionTest
            {
                SessionId = session.Id,
                CatalogTestId = catalogTestId,
                Order = 1
            });
            await db.SaveChangesAsync();
        });

        return session.Id;
    }

    // Sesión de otro PF: SQL crudo con el pf_id ajeno, que el filtro global del
    // AppDbContext vuelve invisible para este token.
    private async Task<Guid> CrearSesionDeOtroPfAsync(
        SeguimientoTestApplicationFactory factory,
        Guid? categoryId = null)
    {
        var sessionId = Guid.NewGuid();

        await factory.ExecuteInDbContextAsync(db => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO field_test_sessions (id, category_id, date, name, status, pf_id, created_at, deleted_at) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, NULL)",
            sessionId,
            categoryId ?? Guid.NewGuid(),
            new DateTime(2026, 11, 9),
            "Sesión ajena",
            "Planned",
            _otherPfId,
            DateTime.UtcNow));

        return sessionId;
    }
}
