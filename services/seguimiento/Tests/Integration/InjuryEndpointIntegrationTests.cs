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
// con el dominio rechazando fechas inválidas o zonas fuera del check-in como 400 con
// mensaje en español.
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

        // El alta escribe su fila inicial en el historial: null → Active con el PF del token
        // (la máquina de estados agrega una fila más por cada transición, KAN-420).
        var initialRow = await factory.ExecuteInDbContextAsync(db =>
            db.AthleteInjuryStatusChanges.IgnoreQueryFilters()
                .Where(row => row.InjuryId == dto.Id)
                .SingleAsync());

        Assert.Null(initialRow.FromStatus);
        Assert.Equal(InjuryStatus.Active, initialRow.ToStatus);
        Assert.Equal(_pfId, initialRow.ChangedBy);
        Assert.False(string.IsNullOrWhiteSpace(initialRow.Note));
    }

    [Fact]
    public async Task Given_ReturnDateBeforeInjuryDate_When_PostInjury_Then_Returns400WithSpanishMessage()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

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

    // Zona fuera del vocabulario de las 19 del check-in (revisión de Enzo).
    [Fact]
    public async Task Given_UnknownZone_When_PostInjury_Then_Returns400WithSpanishMessage()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .PostAsJsonAsync(Endpoint(_athleteId), new CreateAthleteInjuryRequest
            {
                Zone = "femoral",
                Description = "Dolor al estirar",
                InjuryDate = InjuryDate,
                EstimatedReturnDate = EstimatedReturnDate
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem!.Status);
        Assert.Equal(
            "La zona de la lesión no es válida: debe ser una de las 19 zonas del check-in (ej.: isquiotibiales).",
            problem.Title);

        // Nada se persiste: ni la lesión ni su fila inicial de historial.
        var persisted = await factory.ExecuteInDbContextAsync(db =>
            db.AthleteInjuries.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, persisted);

        var history = await factory.ExecuteInDbContextAsync(db =>
            db.AthleteInjuryStatusChanges.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, history);
    }

    // ---------- Listado ----------

    [Fact]
    public async Task Given_Injuries_When_ListInjuries_Then_Returns200WithFilteredStatus()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
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

        var post = await factory.CreateClient().PostAsJsonAsync(Endpoint(_athleteId), RequestValida());
        var get = await factory.CreateClient().GetAsync(Endpoint(_athleteId));

        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
    }

    [Fact]
    public async Task Given_AthleteRoleToken_When_PostInjury_Then_Returns403()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var response = await factory.CreateAuthenticatedClient(_pfId, role: "Atleta")
            .PostAsJsonAsync(Endpoint(_athleteId), RequestValida());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
