using System.Net;
using System.Net.Http.Json;
using Application.DTOs.FieldTests;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Seguimiento.Tests.Infrastructure;

namespace Seguimiento.Tests.Integration;

// Certificación HTTP de KAN-425: la carga rápida de resultados de campo aísla en
// absolutamente todo lo que no es del PF (sesión, atleta y resultado → 404 sin escribir
// nada) y deja que el índice único de la base rechace el duplicado como 409, con un
// camino caliente de 1 SELECT + 1 llamada a Estructura + 1 INSERT por pedido.
public sealed class FieldTestEndpointIntegrationTests
{
    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _otherPfId = Guid.NewGuid();
    private readonly Guid _athleteId = Guid.NewGuid();

    private static string SessionEndpoint(Guid sessionId) =>
        $"api/v1/field-test-sessions/{sessionId}";

    private static string ResultsEndpoint(Guid sessionId) =>
        $"api/v1/field-test-sessions/{sessionId}/results";

    private static string ResultEndpoint(Guid sessionId, Guid resultId) =>
        $"api/v1/field-test-sessions/{sessionId}/results/{resultId}";

    private static CreateFieldTestResultRequest ResultadoValido(
        Guid catalogTestId, Guid athleteId, decimal value = 42.5m, int attempt = 1) =>
        new()
        {
            CatalogTestId = catalogTestId,
            AthleteId = athleteId,
            Value = value,
            Attempt = attempt
        };

    // Id de un test del catálogo sembrado al arrancar el factory.
    private static Task<Guid> ObtenerTestIdAsync(SeguimientoTestApplicationFactory factory) =>
        factory.ExecuteInDbContextAsync(db => db.CatalogTests.Select(test => test.Id).FirstAsync());

    // Sesión propia: se crea por EF para que AppDbContext le asigne el pf_id del token.
    private static async Task<Guid> CrearSesionPropiaAsync(
        SeguimientoTestApplicationFactory factory,
        FieldTestSessionStatus status = FieldTestSessionStatus.InProgress)
    {
        var session = new FieldTestSession
        {
            CategoryId = Guid.NewGuid(),
            Date = new DateTime(2026, 11, 4),
            Name = "Evaluación de pretemporada",
            Status = status
        };

        await factory.ExecuteInDbContextAsync(async db =>
        {
            db.FieldTestSessions.Add(session);
            await db.SaveChangesAsync();
        });

        return session.Id;
    }

    // Sesión y resultado de otro PF: SQL crudo con el pf_id ajeno, que el filtro global
    // del AppDbContext vuelve invisible para este token.
    private async Task<(Guid SessionId, Guid ResultId)> CrearSesionYResultadoAjenosAsync(
        SeguimientoTestApplicationFactory factory, Guid catalogTestId)
    {
        var sessionId = Guid.NewGuid();
        var resultId = Guid.NewGuid();

        await factory.ExecuteInDbContextAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO field_test_sessions (id, category_id, date, name, status, pf_id, created_at, deleted_at) " +
                "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, NULL)",
                sessionId,
                Guid.NewGuid(),
                new DateTime(2026, 11, 4),
                "Sesión ajena",
                "InProgress",
                _otherPfId,
                DateTime.UtcNow);

            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO field_test_results (id, session_id, catalog_test_id, athlete_id, value, attempt, recorded_at, pf_id, created_at, deleted_at) " +
                "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, NULL)",
                resultId,
                sessionId,
                catalogTestId,
                _athleteId,
                42.5m,
                1,
                DateTime.UtcNow,
                _otherPfId,
                DateTime.UtcNow);
        });

        return (sessionId, resultId);
    }

    // Resultado de otro PF apuntando a una sesión propia: el id existe, pero el filtro
    // global lo vuelve invisible para el PUT.
    private async Task<Guid> CrearResultadoAjenoEnSesionPropiaAsync(
        SeguimientoTestApplicationFactory factory, Guid sessionId, Guid catalogTestId)
    {
        var resultId = Guid.NewGuid();

        await factory.ExecuteInDbContextAsync(db => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO field_test_results (id, session_id, catalog_test_id, athlete_id, value, attempt, recorded_at, pf_id, created_at, deleted_at) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, NULL)",
            resultId,
            sessionId,
            catalogTestId,
            _athleteId,
            42.5m,
            1,
            DateTime.UtcNow,
            _otherPfId,
            DateTime.UtcNow));

        return resultId;
    }

    // ---------- POST: carga rápida ----------

    [Fact]
    public async Task Given_ValidResult_When_PostResult_Then_Returns201AndPersists()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);

        var sessionId = await CrearSesionPropiaAsync(factory);
        var testId = await ObtenerTestIdAsync(factory);

        var response = await client.PostAsJsonAsync(
            ResultsEndpoint(sessionId), ResultadoValido(testId, _athleteId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<FieldTestResultDto>();
        Assert.NotNull(dto);
        Assert.Equal(_athleteId, dto!.AthleteId);
        Assert.Equal(testId, dto.CatalogTestId);
        Assert.Equal(42.5m, dto.Value);
        Assert.Equal(1, dto.Attempt);
        Assert.NotEqual(default, dto.RecordedAt);

        var saved = await factory.ExecuteInDbContextAsync(db =>
            db.FieldTestResults.IgnoreQueryFilters().SingleAsync(result => result.Id == dto.Id));

        Assert.Equal(sessionId, saved.SessionId);
        Assert.Equal(_pfId, saved.PfId);
    }

    // KAN-425: atleta de otro PF → 404 y no se escribe ningún resultado.
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_ForeignAthlete_When_PostResult_Then_Returns404AndPersistsNothing()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        // Estructura responde 404: el atleta no existe o es de otro PF.
        factory.Structure.RespondWith(HttpStatusCode.NotFound, "{}");
        var client = factory.CreateAuthenticatedClient(_pfId);

        var sessionId = await CrearSesionPropiaAsync(factory);
        var testId = await ObtenerTestIdAsync(factory);

        var response = await client.PostAsJsonAsync(
            ResultsEndpoint(sessionId), ResultadoValido(testId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("El atleta no existe o no es tuyo.", problem!.Title);

        var persisted = await factory.ExecuteInDbContextAsync(db =>
            db.FieldTestResults.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, persisted);
    }

    // KAN-425: el mismo (sesión, prueba, atleta, intento) cargado dos veces → 409 y una
    // sola fila en la base (el índice único es la última línea de defensa).
    [Fact]
    public async Task Given_DuplicateResult_When_PostedTwice_Then_Returns409AndPersistsSingleRow()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);

        var sessionId = await CrearSesionPropiaAsync(factory);
        var testId = await ObtenerTestIdAsync(factory);

        var first = await client.PostAsJsonAsync(
            ResultsEndpoint(sessionId), ResultadoValido(testId, _athleteId));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            ResultsEndpoint(sessionId), ResultadoValido(testId, _athleteId));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("El registro ya existe.", problem!.Title);

        var persisted = await factory.ExecuteInDbContextAsync(db =>
            db.FieldTestResults.IgnoreQueryFilters()
                .CountAsync(result => result.SessionId == sessionId));
        Assert.Equal(1, persisted);
    }

    [Fact]
    public async Task Given_StructureDown_When_PostResult_Then_Returns503AndPersistsNothing()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.SimulateDown();
        var client = factory.CreateAuthenticatedClient(_pfId);

        var sessionId = await CrearSesionPropiaAsync(factory);
        var testId = await ObtenerTestIdAsync(factory);

        var response = await client.PostAsJsonAsync(
            ResultsEndpoint(sessionId), ResultadoValido(testId, _athleteId));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var persisted = await factory.ExecuteInDbContextAsync(db =>
            db.FieldTestResults.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, persisted);
    }

    // ---------- GET: sesión con sus tests y resultados ----------

    [Fact]
    public async Task Given_ResultsLoaded_When_GetSession_Then_Returns200WithTestsAndResults()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);

        var sessionId = await CrearSesionPropiaAsync(factory);
        var testId = await ObtenerTestIdAsync(factory);

        // El test del catálogo entra a la sesión con su orden de evaluación.
        await factory.ExecuteInDbContextAsync(async db =>
        {
            db.FieldTestSessionTests.Add(new FieldTestSessionTest
            {
                SessionId = sessionId,
                CatalogTestId = testId,
                Order = 1
            });
            await db.SaveChangesAsync();
        });

        // Dos intentos del mismo atleta.
        var first = await client.PostAsJsonAsync(
            ResultsEndpoint(sessionId), ResultadoValido(testId, _athleteId, 42.5m, 1));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            ResultsEndpoint(sessionId), ResultadoValido(testId, _athleteId, 41.8m, 2));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var response = await client.GetAsync(SessionEndpoint(sessionId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<FieldTestSessionDto>();
        Assert.NotNull(dto);
        Assert.Equal(sessionId, dto!.Id);
        Assert.Equal("Evaluación de pretemporada", dto.Name);
        Assert.Equal("InProgress", dto.Status);

        var test = Assert.Single(dto.Tests);
        Assert.Equal(testId, test.CatalogTestId);
        Assert.Equal(1, test.Order);

        Assert.Equal(2, dto.Results.Count);
        Assert.Equal(42.5m, dto.Results[0].Value);
        Assert.Equal(1, dto.Results[0].Attempt);
        Assert.Equal(41.8m, dto.Results[1].Value);
        Assert.Equal(2, dto.Results[1].Attempt);
    }

    [Fact]
    public async Task Given_UnknownSessionId_When_GetSession_Then_Returns404()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        var client = factory.CreateAuthenticatedClient(_pfId);

        var response = await client.GetAsync(SessionEndpoint(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("La sesión de test solicitada no existe.", problem!.Title);
    }

    // ---------- Aislamiento por pf_id (404 en todo lo ajeno) ----------

    // KAN-425: sesión de otro PF → 404 en lectura.
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_SessionOfAnotherPf_When_GetSession_Then_Returns404()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        var client = factory.CreateAuthenticatedClient(_pfId);

        var testId = await ObtenerTestIdAsync(factory);
        var (foreignSessionId, _) = await CrearSesionYResultadoAjenosAsync(factory, testId);

        var response = await client.GetAsync(SessionEndpoint(foreignSessionId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("La sesión de test solicitada no existe.", problem!.Title);
    }

    // KAN-425: sesión de otro PF → 404 en la carga y no se escribe nada.
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_SessionOfAnotherPf_When_PostResult_Then_Returns404AndPersistsNothing()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);

        var testId = await ObtenerTestIdAsync(factory);
        var (foreignSessionId, _) = await CrearSesionYResultadoAjenosAsync(factory, testId);

        var response = await client.PostAsJsonAsync(
            ResultsEndpoint(foreignSessionId), ResultadoValido(testId, _athleteId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("La sesión de test solicitada no existe.", problem!.Title);

        var persisted = await factory.ExecuteInDbContextAsync(db =>
            db.FieldTestResults.IgnoreQueryFilters()
                .CountAsync(result => result.SessionId == foreignSessionId && result.AthleteId == _athleteId));
        Assert.Equal(1, persisted); // Solo la fila sembrada del otro PF; nada nuevo.
    }

    // KAN-425: sesión de otro PF → 404 en la edición y la fila queda intacta.
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_SessionOfAnotherPf_When_PutResult_Then_Returns404AndKeepsValue()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        var client = factory.CreateAuthenticatedClient(_pfId);

        var testId = await ObtenerTestIdAsync(factory);
        var (foreignSessionId, foreignResultId) = await CrearSesionYResultadoAjenosAsync(factory, testId);

        var response = await client.PutAsJsonAsync(
            ResultEndpoint(foreignSessionId, foreignResultId),
            new UpdateFieldTestResultRequest { Value = 10.0m, Attempt = 3 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var saved = await factory.ExecuteInDbContextAsync(db =>
            db.FieldTestResults.IgnoreQueryFilters().SingleAsync(result => result.Id == foreignResultId));
        Assert.Equal(42.5m, saved.Value);
        Assert.Equal(1, saved.Attempt);
        Assert.Equal(_otherPfId, saved.PfId);
    }

    // KAN-425: resultado de otro PF dentro de una sesión propia → 404 y la fila queda intacta.
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_ResultOfAnotherPf_When_PutResult_Then_Returns404AndKeepsValue()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        var client = factory.CreateAuthenticatedClient(_pfId);

        var sessionId = await CrearSesionPropiaAsync(factory);
        var testId = await ObtenerTestIdAsync(factory);
        var foreignResultId = await CrearResultadoAjenoEnSesionPropiaAsync(
            factory, sessionId, testId);

        var response = await client.PutAsJsonAsync(
            ResultEndpoint(sessionId, foreignResultId),
            new UpdateFieldTestResultRequest { Value = 10.0m, Attempt = 3 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("El resultado solicitado no existe.", problem!.Title);

        var saved = await factory.ExecuteInDbContextAsync(db =>
            db.FieldTestResults.IgnoreQueryFilters().SingleAsync(result => result.Id == foreignResultId));
        Assert.Equal(42.5m, saved.Value);
        Assert.Equal(1, saved.Attempt);
    }

    // ---------- PUT: corrección de un resultado ----------

    [Fact]
    public async Task Given_ExistingResult_When_PutResult_Then_Returns200AndUpdates()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);

        var sessionId = await CrearSesionPropiaAsync(factory);
        var testId = await ObtenerTestIdAsync(factory);

        var created = await client.PostAsJsonAsync(
            ResultsEndpoint(sessionId), ResultadoValido(testId, _athleteId, 42.5m, 1));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var resultDto = await created.Content.ReadFromJsonAsync<FieldTestResultDto>();

        var response = await client.PutAsJsonAsync(
            ResultEndpoint(sessionId, resultDto!.Id),
            new UpdateFieldTestResultRequest { Value = 41.8m, Attempt = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<FieldTestResultDto>();
        Assert.Equal(resultDto.Id, dto!.Id);
        Assert.Equal(41.8m, dto.Value);
        Assert.Equal(1, dto.Attempt);

        var saved = await factory.ExecuteInDbContextAsync(db =>
            db.FieldTestResults.IgnoreQueryFilters().SingleAsync(result => result.Id == resultDto.Id));
        Assert.Equal(41.8m, saved.Value);
    }

    // KAN-425: corregir un resultado al intento de otro (mismo atleta y prueba) choca con
    // el índice único → 409 y las dos filas quedan como estaban.
    [Fact]
    public async Task Given_AnotherResultWithSameAttempt_When_PutResult_Then_Returns409AndKeepsValues()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);

        var sessionId = await CrearSesionPropiaAsync(factory);
        var testId = await ObtenerTestIdAsync(factory);

        var first = await client.PostAsJsonAsync(
            ResultsEndpoint(sessionId), ResultadoValido(testId, _athleteId, 42.5m, 1));
        var firstDto = await first.Content.ReadFromJsonAsync<FieldTestResultDto>();

        var second = await client.PostAsJsonAsync(
            ResultsEndpoint(sessionId), ResultadoValido(testId, _athleteId, 41.8m, 2));
        var secondDto = await second.Content.ReadFromJsonAsync<FieldTestResultDto>();

        var response = await client.PutAsJsonAsync(
            ResultEndpoint(sessionId, secondDto!.Id),
            new UpdateFieldTestResultRequest { Value = 40.0m, Attempt = 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Ninguna de las dos filas cambió.
        var results = await factory.ExecuteInDbContextAsync(db =>
            db.FieldTestResults.IgnoreQueryFilters()
                .Where(result => result.SessionId == sessionId)
                .OrderBy(result => result.Attempt)
                .ToListAsync());

        Assert.Equal(2, results.Count);
        Assert.Equal(42.5m, results[0].Value);
        Assert.Equal(41.8m, results[1].Value);
        Assert.Equal(firstDto!.Id, results[0].Id);
        Assert.Equal(secondDto.Id, results[1].Id);
    }

    // ---------- Autorización (contrato: 401/403) ----------

    [Fact]
    public async Task Given_NoToken_When_PostResult_Then_Returns401()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var response = await factory.CreateClient().PostAsJsonAsync(
            ResultsEndpoint(Guid.NewGuid()),
            ResultadoValido(Guid.NewGuid(), _athleteId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Given_AthleteRoleToken_When_PostResult_Then_Returns403()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var response = await factory
            .CreateAuthenticatedClient(_pfId, role: "Atleta")
            .PostAsJsonAsync(
                ResultsEndpoint(Guid.NewGuid()),
                ResultadoValido(Guid.NewGuid(), _athleteId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
