using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Injuries;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Seguimiento.Tests.Infrastructure;

namespace Seguimiento.Tests.Integration;

// Certificación HTTP de KAN-419: POST crea la lesión en Active y GET lista las del atleta,
// con el rol PreparadorFisico exigido por contrato (401 sin token, 403 con rol Atleta) y
// con el dominio rechazando las fechas inválidas como 400 con mensaje en español.
public sealed class InjuryEndpointIntegrationTests
{
    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _athleteId = Guid.NewGuid();

    private static readonly DateTime InjuryDate = new(2026, 10, 20);
    private static readonly DateTime EstimatedReturnDate = new(2026, 11, 10);

    private static string Endpoint(Guid athleteId) =>
        $"api/v1/athletes/{athleteId}/injuries";

    private static CreateAthleteInjuryRequest RequestValida() =>
        new()
        {
            Zone = "isquiotibiales",
            Description = "Gradación 3 tras sprint",
            InjuryDate = InjuryDate,
            EstimatedReturnDate = EstimatedReturnDate
        };

    // ---------- Alta ----------

    [Fact]
    public async Task Given_ValidInjury_When_PostInjury_Then_Returns201ActiveAndPersists()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .PostAsJsonAsync(Endpoint(_athleteId), RequestValida());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<AthleteInjuryDto>();
        Assert.NotNull(dto);
        Assert.Equal("Active", dto!.Status);
        Assert.Equal(_athleteId, dto.AthleteId);
        Assert.Equal("isquiotibiales", dto.Zone);
        Assert.Equal(InjuryDate, dto.InjuryDate);
        Assert.Equal(EstimatedReturnDate, dto.EstimatedReturnDate);
        Assert.Null(dto.ClearedAt);

        var saved = await factory.ExecuteInDbContextAsync(db =>
            db.AthleteInjuries.IgnoreQueryFilters().SingleAsync(injury => injury.Id == dto.Id));

        Assert.Equal(InjuryStatus.Active, saved.Status);
        Assert.Equal(_pfId, saved.PfId);

        // El alta no escribe filas en el historial: eso lo hace la máquina de estados (KAN-420).
        var historyCount = await factory.ExecuteInDbContextAsync(db =>
            db.AthleteInjuryStatusChanges.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, historyCount);
    }

    [Fact]
    public async Task Given_ReturnDateBeforeInjuryDate_When_PostInjury_Then_Returns400WithSpanishMessage()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .PostAsJsonAsync(Endpoint(_athleteId), new CreateAthleteInjuryRequest
            {
                Zone = "isquiotibiales",
                Description = "Dolor al estirar",
                InjuryDate = InjuryDate,
                EstimatedReturnDate = InjuryDate.AddDays(-1)
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem!.Status);
        Assert.Equal("La fecha de retorno estimada debe ser posterior a la fecha de la lesión.", problem.Title);

        var persisted = await factory.ExecuteInDbContextAsync(db =>
            db.AthleteInjuries.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, persisted);
    }

    [Fact]
    public async Task Given_WithoutEstimatedReturnDate_When_PostInjury_Then_Returns400WithSpanishMessage()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .PostAsJsonAsync(Endpoint(_athleteId), new CreateAthleteInjuryRequest
            {
                Zone = "isquiotibiales",
                Description = "Dolor al estirar",
                InjuryDate = InjuryDate,
                EstimatedReturnDate = null
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("La fecha de retorno estimada es obligatoria.", problem!.Title);
    }

    // ---------- Listado ----------

    [Fact]
    public async Task Given_Injuries_When_ListInjuries_Then_Returns200WithFilteredStatus()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        var client = factory.CreateAuthenticatedClient(_pfId);

        var first = await client.PostAsJsonAsync(Endpoint(_athleteId), RequestValida());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(Endpoint(_athleteId), new CreateAthleteInjuryRequest
        {
            Zone = "cuadriceps",
            Description = "Molestia en trote largo",
            InjuryDate = InjuryDate,
            EstimatedReturnDate = EstimatedReturnDate
        });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var all = await client.GetFromJsonAsync<List<AthleteInjuryDto>>(Endpoint(_athleteId));
        Assert.NotNull(all);
        Assert.Equal(2, all!.Count);

        // Filtro opcional por estado (contrato: listAthleteInjuries?status=...).
        var active = await client.GetFromJsonAsync<List<AthleteInjuryDto>>(
            $"{Endpoint(_athleteId)}?status=Active");
        Assert.Equal(2, active!.Count);

        var inRecovery = await client.GetFromJsonAsync<List<AthleteInjuryDto>>(
            $"{Endpoint(_athleteId)}?status=InRecovery");
        Assert.Empty(inRecovery!);
    }

    [Fact]
    public async Task Given_NoInjuriesForOtherAthlete_When_ListInjuries_Then_Returns200WithEmptyList()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_athleteId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<AthleteInjuryDto>>();
        Assert.Empty(list!);
    }

    // ---------- Autorización (contrato: 401/403) ----------

    [Fact]
    public async Task Given_NoToken_When_PostInjury_Then_Returns401()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();

        var post = await factory.CreateClient().PostAsJsonAsync(Endpoint(_athleteId), RequestValida());
        var get = await factory.CreateClient().GetAsync(Endpoint(_athleteId));

        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
    }

    [Fact]
    public async Task Given_AthleteRoleToken_When_PostInjury_Then_Returns403()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();

        var response = await factory.CreateAuthenticatedClient(_pfId, role: "Atleta")
            .PostAsJsonAsync(Endpoint(_athleteId), RequestValida());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- Aislamiento: atleta de otro PF (KAN-431) ----------

    [Fact]
    public async Task Given_AthleteOfAnotherPf_When_PostInjury_Then_Returns404AndPersistsNothing()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        // Estructura responde 404: el atleta no existe o es de otro PF.
        factory.Structure.RespondWith(HttpStatusCode.NotFound, "{}");

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .PostAsJsonAsync(Endpoint(_athleteId), RequestValida());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.EndsWith($"api/v1/athletes/{_athleteId}", factory.Structure.LastRequest!.RequestUri!.ToString());

        var injuriesCount = await factory.ExecuteInDbContextAsync(db =>
            db.AthleteInjuries.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, injuriesCount);
    }

    [Fact]
    public async Task Given_AthleteOfAnotherPf_When_ListInjuries_Then_Returns404NotEmptyList()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondWith(HttpStatusCode.NotFound, "{}");

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_athleteId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Given_StructureDown_When_PostInjury_Then_Returns503AndPersistsNothing()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.SimulateDown();

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .PostAsJsonAsync(Endpoint(_athleteId), RequestValida());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var injuriesCount = await factory.ExecuteInDbContextAsync(db =>
            db.AthleteInjuries.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, injuriesCount);
    }
}
