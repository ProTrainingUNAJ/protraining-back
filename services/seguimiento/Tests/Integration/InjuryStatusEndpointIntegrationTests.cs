using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.DTOs.Injuries;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Seguimiento.Tests.Infrastructure;

namespace Seguimiento.Tests.Integration;

// Certificación HTTP de KAN-420: el PATCH de la máquina de estados estricta rechaza con 400
// el salto directo de Activa a Alta médica (CA1) y permite el alta en dos pasos con nota
// clínica, escribiendo una fila por cambio en el historial y liberando al atleta con
// cleared_at (CA2). Los enums viajan como su nombre, igual que los envía el front.
public sealed class InjuryStatusEndpointIntegrationTests
{
    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _athleteId = Guid.NewGuid();

    // Texto literal que debe recibir quien hace el pedido (CA1).
    private const string MensajeSaltoDirecto =
        "Una lesión activa no puede pasar directamente a 'Alta médica'. Debe pasar primero por 'En recuperación'.";

    // Serialización del cuerpo igual a la del contrato: {"newStatus": "InRecovery"} y no un
    // número, que es exactamente lo que envía el SDK generado del front.
    private static readonly JsonSerializerOptions EnumeradosComoTexto = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static string Endpoint(Guid athleteId) =>
        $"api/v1/athletes/{athleteId}/injuries";

    private static string StatusEndpoint(Guid injuryId) =>
        $"api/v1/injuries/{injuryId}/status";

    private static CreateAthleteInjuryRequest RequestValida() =>
        new()
        {
            Zone = "isquiotibiales",
            Description = "Gradación 3 tras sprint",
            InjuryDate = new DateTime(2026, 10, 20),
            EstimatedReturnDate = new DateTime(2026, 11, 10)
        };

    private async Task<Guid> CrearLesionActivaAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(Endpoint(_athleteId), RequestValida());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<AthleteInjuryDto>();
        Assert.NotNull(dto);
        return dto!.Id;
    }

    private static Task<HttpResponseMessage> CambiarEstadoAsync(
        HttpClient client,
        Guid injuryId,
        InjuryStatus newStatus,
        string? transitionNotes) =>
        client.PatchAsJsonAsync(
            StatusEndpoint(injuryId),
            new UpdateInjuryStatusRequest
            {
                NewStatus = newStatus,
                TransitionNotes = transitionNotes
            },
            EnumeradosComoTexto);

    // ---------- CA1: prohibido el salto directo ----------

    [Fact]
    public async Task Given_ActiveInjury_When_PatchDirectlyToMedicallyCleared_Then_Returns400WithRecoveryMessageAndPersistsNothing()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);
        var injuryId = await CrearLesionActivaAsync(client);

        var response = await CambiarEstadoAsync(
            client, injuryId, InjuryStatus.MedicallyCleared, "Alta directa sin readaptación.");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem!.Status);
        Assert.Equal(MensajeSaltoDirecto, problem.Title);

        // La lesión queda intacta y no se escribe ninguna fila de historial.
        var saved = await factory.ExecuteInDbContextAsync(db =>
            db.AthleteInjuries.IgnoreQueryFilters().SingleAsync(injury => injury.Id == injuryId));

        Assert.Equal(InjuryStatus.Active, saved.Status);
        Assert.Null(saved.ClearedAt);

        var historyCount = await factory.ExecuteInDbContextAsync(db =>
            db.AthleteInjuryStatusChanges.IgnoreQueryFilters().CountAsync(c => c.InjuryId == injuryId));

        Assert.Equal(0, historyCount);
    }

    // ---------- CA2: alta en dos pasos con nota clínica ----------

    [Fact]
    public async Task Given_ActiveInjury_When_PatchThroughRecovery_Then_Returns200ClearsAthleteAndWritesHistory()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);
        var injuryId = await CrearLesionActivaAsync(client);

        const string notaRecuperacion = "Inicio de readaptación con carga progresiva.";
        const string notaAlta = "Alta médica confirmada tras reevaluación.";

        // Paso 1: Activa → En recuperación.
        var aRecuperacion = await CambiarEstadoAsync(
            client, injuryId, InjuryStatus.InRecovery, notaRecuperacion);

        Assert.Equal(HttpStatusCode.OK, aRecuperacion.StatusCode);
        var dtoRecuperacion = await aRecuperacion.Content.ReadFromJsonAsync<AthleteInjuryDto>();
        Assert.Equal("InRecovery", dtoRecuperacion!.Status);
        Assert.Null(dtoRecuperacion.ClearedAt);

        // Paso 2: En recuperación → Alta médica (el atleta queda liberado).
        var aAlta = await CambiarEstadoAsync(
            client, injuryId, InjuryStatus.MedicallyCleared, notaAlta);

        Assert.Equal(HttpStatusCode.OK, aAlta.StatusCode);
        var dtoAlta = await aAlta.Content.ReadFromJsonAsync<AthleteInjuryDto>();
        Assert.Equal("MedicallyCleared", dtoAlta!.Status);
        Assert.NotNull(dtoAlta.ClearedAt);

        // Una fila por cambio, con el pf del token como autor y la nota cargada.
        var history = await factory.ExecuteInDbContextAsync(db =>
            db.AthleteInjuryStatusChanges.IgnoreQueryFilters()
                .Where(c => c.InjuryId == injuryId)
                .ToListAsync());

        Assert.Equal(2, history.Count);
        Assert.All(history, c => Assert.Equal(_pfId, c.ChangedBy));
        Assert.All(history, c => Assert.NotEqual(default, c.ChangedAt));
        Assert.Contains(history, c =>
            c.FromStatus == InjuryStatus.Active &&
            c.ToStatus == InjuryStatus.InRecovery &&
            c.Note == notaRecuperacion);
        Assert.Contains(history, c =>
            c.FromStatus == InjuryStatus.InRecovery &&
            c.ToStatus == InjuryStatus.MedicallyCleared &&
            c.Note == notaAlta);

        // El listado del atleta refleja el estado nuevo.
        var list = await client.GetFromJsonAsync<List<AthleteInjuryDto>>(Endpoint(_athleteId));
        Assert.Equal("MedicallyCleared", list!.Single(injury => injury.Id == injuryId).Status);
    }

    // ---------- Nota clínica obligatoria ----------

    [Fact]
    public async Task Given_MissingTransitionNotes_When_PatchStatus_Then_Returns400WithSpanishMessage()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);
        var injuryId = await CrearLesionActivaAsync(client);

        var response = await CambiarEstadoAsync(client, injuryId, InjuryStatus.InRecovery, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(
            problem!.Errors.Values.SelectMany(messages => messages),
            message => message.Contains("La nota clínica es obligatoria."));

        var saved = await factory.ExecuteInDbContextAsync(db =>
            db.AthleteInjuries.IgnoreQueryFilters().SingleAsync(injury => injury.Id == injuryId));
        Assert.Equal(InjuryStatus.Active, saved.Status);
    }

    // ---------- 404: lesión inexistente o de otro PF ----------

    [Fact]
    public async Task Given_UnknownInjuryId_When_PatchStatus_Then_Returns404()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);

        var response = await CambiarEstadoAsync(
            client, Guid.NewGuid(), InjuryStatus.InRecovery, "Nota clínica.");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("La lesión solicitada no existe.", problem!.Title);
    }

    [Fact]
    public async Task Given_InjuryBelongingToAnotherPf_When_PatchStatus_Then_Returns404()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);

        // Lesión sembrada a mano con el pf_id de otro preparador: el filtro global del
        // AppDbContext la vuelve invisible para este token.
        var injuryId = Guid.NewGuid();
        await factory.ExecuteInDbContextAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO athlete_injuries (id, athlete_id, zone, description, injury_date, estimated_return_date, status, cleared_at, pf_id, created_at, deleted_at) " +
                "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, NULL, {7}, {8}, NULL)",
                injuryId,
                _athleteId,
                "isquiotibiales",
                "Gradación 3 tras sprint",
                new DateTime(2026, 10, 20),
                new DateTime(2026, 11, 10),
                (int)InjuryStatus.Active,
                Guid.NewGuid(),
                DateTime.UtcNow);
        });

        var response = await CambiarEstadoAsync(
            client, injuryId, InjuryStatus.InRecovery, "Nota clínica.");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("La lesión solicitada no existe.", problem!.Title);
    }

    // ---------- Autorización (contrato: 401/403) ----------

    [Fact]
    public async Task Given_NoToken_When_PatchStatus_Then_Returns401()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();

        var response = await factory.CreateClient().PatchAsJsonAsync(
            StatusEndpoint(Guid.NewGuid()),
            new UpdateInjuryStatusRequest
            {
                NewStatus = InjuryStatus.InRecovery,
                TransitionNotes = "Nota clínica."
            },
            EnumeradosComoTexto);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Given_AthleteRoleToken_When_PatchStatus_Then_Returns403()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var injuryId = await CrearLesionActivaAsync(factory.CreateAuthenticatedClient(_pfId));

        var response = await CambiarEstadoAsync(
            factory.CreateAuthenticatedClient(_pfId, role: "Atleta"),
            injuryId,
            InjuryStatus.InRecovery,
            "Nota clínica.");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
