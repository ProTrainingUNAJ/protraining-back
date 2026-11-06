using System.Net;
using System.Net.Http.Json;
using Application.DTOs.FieldTests;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Seguimiento.Tests.Infrastructure;

namespace Seguimiento.Tests.Integration;

// Certificación HTTP de KAN-426: la confirmación de un resultado de fuerza envía el 1RM
// testeado a Entrenamiento de forma síncrona (200 solo si Entrenamiento lo guardó),
// aísla todo lo ajeno con 404, responde 400 a los tests sin mapeo y 503 si Entrenamiento
// no responde tras los reintentos de Polly.
public sealed class FieldTestConfirmEndpointIntegrationTests
{
    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _athleteId = Guid.NewGuid();

    private static string ConfirmEndpoint(Guid sessionId, Guid resultId) =>
        $"api/v1/field-test-sessions/{sessionId}/results/{resultId}/confirm";

    // El test sembrado que tiene mapeo a ejercicio de Entrenamiento (KAN-426).
    private static Task<Guid> ObtenerTestIdMapeadoAsync(SeguimientoTestApplicationFactory factory) =>
        factory.ExecuteInDbContextAsync(db => db.CatalogTests
            .Where(test => test.Name == "Sentadilla 1RM")
            .Select(test => test.Id)
            .FirstAsync());

    // El primer test del catálogo sin mapeo (ninguno de los no-fuerza tiene objetivo).
    private static Task<(Guid Id, string Name)> ObtenerTestIdSinMapeoAsync(SeguimientoTestApplicationFactory factory) =>
        factory.ExecuteInDbContextAsync(async db =>
        {
            var test = await db.CatalogTests
                .Where(test => test.TargetExerciseName == null)
                .FirstAsync();
            return (test.Id, test.Name);
        });

    private static async Task<Guid> CrearSesionPropiaAsync(SeguimientoTestApplicationFactory factory)
    {
        var session = new FieldTestSession
        {
            CategoryId = Guid.NewGuid(),
            Date = new DateTime(2026, 11, 4),
            Name = "Evaluación de pretemporada",
            Status = FieldTestSessionStatus.InProgress
        };

        await factory.ExecuteInDbContextAsync(async db =>
        {
            db.FieldTestSessions.Add(session);
            await db.SaveChangesAsync();
        });

        return session.Id;
    }

    // Resultado cargado en la sesión propia (como lo dejaría el POST de KAN-425).
    private async Task<Guid> CrearResultadoAsync(
        SeguimientoTestApplicationFactory factory, Guid sessionId, Guid catalogTestId, decimal value = 120.0m)
    {
        var result = new FieldTestResult
        {
            SessionId = sessionId,
            CatalogTestId = catalogTestId,
            AthleteId = _athleteId,
            Value = value,
            Attempt = 1,
            RecordedAt = DateTime.UtcNow
        };

        await factory.ExecuteInDbContextAsync(async db =>
        {
            db.FieldTestResults.Add(result);
            await db.SaveChangesAsync();
        });

        return result.Id;
    }

    // Sesión y resultado de otro PF: SQL crudo con el pf_id ajeno, invisible por el filtro.
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
                sessionId, Guid.NewGuid(), new DateTime(2026, 11, 4), "Sesión ajena",
                "InProgress", Guid.NewGuid(), DateTime.UtcNow);

            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO field_test_results (id, session_id, catalog_test_id, athlete_id, value, attempt, recorded_at, pf_id, created_at, deleted_at) " +
                "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, NULL)",
                resultId, sessionId, catalogTestId, _athleteId, 120.0m, 1,
                DateTime.UtcNow, Guid.NewGuid(), DateTime.UtcNow);
        });

        return (sessionId, resultId);
    }

    // ---------- Confirmación feliz (AC) ----------

    // KAN-426 (AC): DADO un test de Sentadilla de 120 kg CUANDO se confirma ENTONCES
    // Entrenamiento recibe el 1RM con origen testeado (TestedInSession).
    [Fact]
    public async Task Given_ForceResult_When_Confirm_Then_Returns200AndSendsTestedRecord()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        var exerciseId = Guid.NewGuid();
        factory.Entrenamiento.WithExercise("Sentadilla con barra", exerciseId);
        factory.Entrenamiento.RespondOk();
        var client = factory.CreateAuthenticatedClient(_pfId);

        var sessionId = await CrearSesionPropiaAsync(factory);
        var testId = await ObtenerTestIdMapeadoAsync(factory);
        var resultId = await CrearResultadoAsync(factory, sessionId, testId);

        var response = await client.PostAsync(ConfirmEndpoint(sessionId, resultId), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<FieldTestResultDto>();
        Assert.Equal(resultId, dto!.Id);
        Assert.Equal(120.0m, dto.Value);

        // Entrenamiento recibió exactamente el 1RM testeado del atleta.
        var record = Assert.Single(factory.Entrenamiento.SavedRecords);
        Assert.Equal(_athleteId, record.AthleteId);
        Assert.Equal(exerciseId, record.ExerciseId);
        Assert.Equal(120.0m, record.Weight);
        Assert.Equal("TestedInSession", record.Origin);

        // La primera confirmación resuelve por nombre y cachea el GUID en el catálogo.
        Assert.Equal(1, factory.Entrenamiento.ExerciseSearchCalls);
        var cached = await factory.ExecuteInDbContextAsync(db =>
            db.CatalogTests.IgnoreQueryFilters().SingleAsync(test => test.Id == testId));
        Assert.Equal(exerciseId, cached.ExerciseId);
    }

    // KAN-426: con el GUID cacheado, la segunda confirmación va directo al guardado
    // (sin volver a buscar el ejercicio) y vuelve a enviar el mismo valor.
    [Fact]
    public async Task Given_CachedExercise_When_ConfirmAgain_Then_SkipsSearchAndResends()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        var exerciseId = Guid.NewGuid();
        factory.Entrenamiento.WithExercise("Sentadilla con barra", exerciseId);
        factory.Entrenamiento.RespondOk();
        var client = factory.CreateAuthenticatedClient(_pfId);

        var sessionId = await CrearSesionPropiaAsync(factory);
        var testId = await ObtenerTestIdMapeadoAsync(factory);
        var resultId = await CrearResultadoAsync(factory, sessionId, testId);

        var first = await client.PostAsync(ConfirmEndpoint(sessionId, resultId), null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsync(ConfirmEndpoint(sessionId, resultId), null);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        Assert.Equal(2, factory.Entrenamiento.SaveRecordCalls);
        Assert.Equal(1, factory.Entrenamiento.ExerciseSearchCalls); // solo la primera vez
    }

    // ---------- Test sin mapeo → 400 ----------

    [Fact]
    public async Task Given_ResultOfNonForceTest_When_Confirm_Then_Returns400WithoutCallingEntrenamiento()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Entrenamiento.RespondOk();
        var client = factory.CreateAuthenticatedClient(_pfId);

        var sessionId = await CrearSesionPropiaAsync(factory);
        var (testId, testName) = await ObtenerTestIdSinMapeoAsync(factory);
        var resultId = await CrearResultadoAsync(factory, sessionId, testId);

        var response = await client.PostAsync(ConfirmEndpoint(sessionId, resultId), null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Este test no genera récord 1RM.", problem!.Title);

        Assert.Equal(0, factory.Entrenamiento.ExerciseSearchCalls);
        Assert.Equal(0, factory.Entrenamiento.SaveRecordCalls);
        Assert.NotEmpty(testName);
    }

    // El ejercicio mapeado no existe en Entrenamiento → 400, sin enviar nada.
    [Fact]
    public async Task Given_ExerciseMissingInEntrenamiento_When_Confirm_Then_Returns400()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Entrenamiento.RespondOk(); // responde OK pero sin ningún ejercicio
        var client = factory.CreateAuthenticatedClient(_pfId);

        var sessionId = await CrearSesionPropiaAsync(factory);
        var testId = await ObtenerTestIdMapeadoAsync(factory);
        var resultId = await CrearResultadoAsync(factory, sessionId, testId);

        var response = await client.PostAsync(ConfirmEndpoint(sessionId, resultId), null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Entrenamiento.SaveRecordCalls);
    }

    // ---------- Entrenamiento no disponible → 503 (decisión: propagar) ----------

    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_EntrenamientoDown_When_Confirm_Then_Returns503()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Entrenamiento.SimulateDown();
        var client = factory.CreateAuthenticatedClient(_pfId);

        var sessionId = await CrearSesionPropiaAsync(factory);
        var testId = await ObtenerTestIdMapeadoAsync(factory);
        var resultId = await CrearResultadoAsync(factory, sessionId, testId);

        var response = await client.PostAsync(ConfirmEndpoint(sessionId, resultId), null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Entrenamiento no disponible.", problem!.Title);
    }

    // Rejección del pipeline de Polly (breaker abierto) → también 503, nunca 500.
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_OpenCircuitBreaker_When_Confirm_Then_Returns503()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Entrenamiento.SimulateOpenCircuit();
        var client = factory.CreateAuthenticatedClient(_pfId);

        var sessionId = await CrearSesionPropiaAsync(factory);
        var testId = await ObtenerTestIdMapeadoAsync(factory);
        var resultId = await CrearResultadoAsync(factory, sessionId, testId);

        var response = await client.PostAsync(ConfirmEndpoint(sessionId, resultId), null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // ---------- Aislamiento por pf_id (404 en todo lo ajeno) ----------

    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_SessionOfAnotherPf_When_Confirm_Then_Returns404WithoutCallingEntrenamiento()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Entrenamiento.RespondOk();
        var client = factory.CreateAuthenticatedClient(_pfId);

        var testId = await ObtenerTestIdMapeadoAsync(factory);
        var (foreignSessionId, _) = await CrearSesionYResultadoAjenosAsync(factory, testId);

        var response = await client.PostAsync(
            ConfirmEndpoint(foreignSessionId, Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, factory.Entrenamiento.SaveRecordCalls);
    }

    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_ResultOfAnotherPf_When_Confirm_Then_Returns404WithoutCallingEntrenamiento()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Entrenamiento.RespondOk();
        var client = factory.CreateAuthenticatedClient(_pfId);

        var testId = await ObtenerTestIdMapeadoAsync(factory);
        var (foreignSessionId, foreignResultId) = await CrearSesionYResultadoAjenosAsync(factory, testId);

        // La sesión existe (es ajena): el 404 lo debe dar el resultado dentro de ella.
        var response = await client.PostAsync(
            ConfirmEndpoint(foreignSessionId, foreignResultId), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, factory.Entrenamiento.SaveRecordCalls);
    }

    // Resultado de otro PF apuntando a una sesión propia → 404 (filtro global).
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_ForeignResultInOwnSession_When_Confirm_Then_Returns404()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Entrenamiento.RespondOk();
        var client = factory.CreateAuthenticatedClient(_pfId);

        var sessionId = await CrearSesionPropiaAsync(factory);
        var testId = await ObtenerTestIdMapeadoAsync(factory);

        var foreignResultId = Guid.NewGuid();
        await factory.ExecuteInDbContextAsync(db => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO field_test_results (id, session_id, catalog_test_id, athlete_id, value, attempt, recorded_at, pf_id, created_at, deleted_at) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, NULL)",
            foreignResultId, sessionId, testId, _athleteId, 120.0m, 1,
            DateTime.UtcNow, Guid.NewGuid(), DateTime.UtcNow));

        var response = await client.PostAsync(ConfirmEndpoint(sessionId, foreignResultId), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, factory.Entrenamiento.SaveRecordCalls);
    }

    // ---------- Autorización (contrato: 401/403) ----------

    [Fact]
    public async Task Given_NoToken_When_Confirm_Then_Returns401()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var response = await factory.CreateClient().PostAsync(
            ConfirmEndpoint(Guid.NewGuid(), Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Given_AthleteRoleToken_When_Confirm_Then_Returns403()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var response = await factory
            .CreateAuthenticatedClient(_pfId, role: "Atleta")
            .PostAsync(ConfirmEndpoint(Guid.NewGuid(), Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
