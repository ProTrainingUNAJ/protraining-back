using System.Net;
using System.Net.Http.Json;
using Application.DTOs.AthleteRecords;
using Application.DTOs.Exercise;
using Application.DTOs.TrainingPlans;
using Application.DTOs.TrainingSessions;
using Domain.Entities;
using Entrenamiento.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Entrenamiento.Tests.Integration;

[Trait("Category", "Isolation")]
public sealed class MultitenantIsolationTests : IAsyncLifetime
{
    private readonly Guid _pfAId = Guid.NewGuid();
    private readonly Guid _pfBId = Guid.NewGuid();

    private EntrenamientoTestApplicationFactory _factory = null!;
    private HttpClient _clientPfA = null!;
    private HttpClient _clientPfB = null!;
    private PfSeedData _dataB = null!;

    public async Task InitializeAsync()
    {
        _factory = new EntrenamientoTestApplicationFactory();
        _clientPfA = _factory.CreateAuthenticatedClient(_pfAId);
        _clientPfB = _factory.CreateAuthenticatedClient(_pfBId);

        // Sembrar datos pertenecientes a PF B
        _dataB = await _factory.SeedPfDataAsync(_pfBId);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task GetTrainingPlans_PfAConsultando_NoIncluyePlanesDePfB()
    {
        // Act
        var response = await _clientPfA.GetAsync("/api/v1/training-plans");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var plans = await response.Content.ReadFromJsonAsync<List<TrainingPlanSummaryDto>>();
        plans.Should().NotBeNull();
        plans!.Should().NotContain(p => p.Id == _dataB.Plan.Id);
    }

    [Fact]
    public async Task GetTrainingPlanById_PfAIntentandoLeerPlanDePfB_Retorna404()
    {
        // Act
        var response = await _clientPfA.GetAsync($"/api/v1/training-plans/{_dataB.Plan.Id}");

        // Assert: Regla de oro KAN-429: siempre 404 para no filtrar existencia
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateTrainingPlan_PfAIntentandoModificarPlanDePfB_Retorna404YNoModificaLaBase()
    {
        // Arrange
        var request = new SaveTrainingPlanRequest(
            Name: "Plan Hackeado por PF A",
            Objective: "Nuevo Objetivo",
            Weeks: new List<TrainingWeekRequest>()
        );

        // Act
        var response = await _clientPfA.PutAsJsonAsync($"/api/v1/training-plans/{_dataB.Plan.Id}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var dbContext = _factory.CreateDbContext(overridePfId: null);
        var planEnBase = await dbContext.TrainingPlans
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == _dataB.Plan.Id);

        planEnBase.Should().NotBeNull();
        planEnBase!.Name.Should().Be(_dataB.Plan.Name);
    }

    [Fact]
    public async Task GetSchedule_PfAConsultando_NoRetornaSesionesDePfB()
    {
        // Act: La asignación de B fue sembrada para DateTime.UtcNow.Date.AddDays(-7), Monday
        var queryDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-7));
        var response = await _clientPfA.GetAsync($"/api/v1/training-plans/schedule?date={queryDate:yyyy-MM-dd}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var schedule = await response.Content.ReadFromJsonAsync<List<ScheduledSessionDto>>();
        schedule.Should().NotBeNull();
        schedule!.Should().NotContain(s => s.SessionId == _dataB.Session.Id);
    }

    [Fact]
    public async Task AssignPlan_PfAIntentandoAsignarPlanDePfB_Retorna404YNoCreaAsignacion()
    {
        // Arrange
        var categoryDeA = Guid.NewGuid();
        _factory.Structure.RegisterCategory(categoryDeA, _pfAId);

        var request = new AssignPlanRequest(
            TrainingPlanId: _dataB.Plan.Id,
            StartDate: DateOnly.FromDateTime(DateTime.UtcNow),
            CategoryId: categoryDeA);

        // Act
        var response = await _clientPfA.PostAsJsonAsync("/api/v1/training-plans/assignments", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var dbContext = _factory.CreateDbContext(overridePfId: null);
        var asignacionEnBase = await dbContext.PlanAssignments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TrainingPlanId == _dataB.Plan.Id && a.CategoryId == categoryDeA);

        asignacionEnBase.Should().BeNull();
    }

    [Fact]
    public async Task GetTrainingSession_PfAIntentandoLeerSesionDePfB_Retorna404()
    {
        // Act
        var response = await _clientPfA.GetAsync($"/api/v1/training-sessions/{_dataB.Session.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateCoachNotes_PfAIntentandoModificarNotasDeSesionDePfB_Retorna404YNoModificaLaBase()
    {
        // Arrange
        var request = new UpdateCoachNotesRequest("Notas modificadas por PF A ilegítimamente");

        // Act
        var response = await _clientPfA.PutAsJsonAsync($"/api/v1/training-sessions/{_dataB.Session.Id}/notes", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var dbContext = _factory.CreateDbContext(overridePfId: null);
        var sesionEnBase = await dbContext.TrainingSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == _dataB.Session.Id);

        sesionEnBase.Should().NotBeNull();
        sesionEnBase!.CoachNotes.Should().Be(_dataB.Session.CoachNotes);
    }

    [Fact]
    public async Task GetTodayRoutine_PfAIntentandoLeerRutinaDeAtletaDePfB_Retorna404()
    {
        // Act
        var response = await _clientPfA.GetAsync($"/api/v1/athletes/{_dataB.AthleteId}/today-routine");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAthleteRecords_PfAIntentandoLeerRecordsDeAtletaDePfB_Retorna404()
    {
        // Act
        var response = await _clientPfA.GetAsync($"/api/v1/athletes/{_dataB.AthleteId}/records");

        // Assert: El atleta es de una categoría no perteneciente a PF A, el guard debe responder 404
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RegisterAthleteRecord_PfAIntentandoCrearRecordParaAtletaDePfB_Retorna404()
    {
        // Arrange
        var request = new SaveAthleteRecordRequest(
            ExerciseId: _dataB.CatalogExercise.Id,
            WeightOneRepMaxKg: 120m,
            Origin: "TestedInSession"
        );

        // Act
        var response = await _clientPfA.PostAsJsonAsync($"/api/v1/athletes/{_dataB.AthleteId}/records", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var dbContext = _factory.CreateDbContext(overridePfId: null);
        var recordEnBase = await dbContext.AthleteRecords
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.AthleteId == _dataB.AthleteId && r.Weight == 120m);

        recordEnBase.Should().BeNull();
    }

    [Fact]
    public async Task GetExercises_PfAVeEjerciciosDeCatalogoPeroNoLosCustomDePfB()
    {
        // Act
        var response = await _clientPfA.GetAsync("/api/v1/exercises");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagedResponse = await response.Content.ReadFromJsonAsync<PaginatedExerciseResponse>();
        pagedResponse.Should().NotBeNull();

        // Debe ver el catálogo (PfId == Guid.Empty)
        pagedResponse!.Items.Should().Contain(e => e.Id == _dataB.CatalogExercise.Id);

        // NO debe ver el ejercicio personalizado creado por PF B
        pagedResponse.Items.Should().NotContain(e => e.Id == _dataB.CustomExercise.Id);
    }

    [Fact]
    public async Task PfB_AccediendoASusPropiosDatos_Obtiene200YDatosCorrectos()
    {
        // Act & Assert para Plan
        var planResp = await _clientPfB.GetAsync($"/api/v1/training-plans/{_dataB.Plan.Id}");
        planResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act & Assert para Sesión
        var sessionResp = await _clientPfB.GetAsync($"/api/v1/training-sessions/{_dataB.Session.Id}");
        sessionResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act & Assert para Ejercicios: PF B ve tanto el de catálogo como el suyo custom
        var exResp = await _clientPfB.GetAsync("/api/v1/exercises");
        exResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await exResp.Content.ReadFromJsonAsync<PaginatedExerciseResponse>();
        paged!.Items.Should().Contain(e => e.Id == _dataB.CatalogExercise.Id);
        paged.Items.Should().Contain(e => e.Id == _dataB.CustomExercise.Id);
    }

    [Fact]
    public async Task GetInternalAthletesWithSession_PfAConsultando_NoIncluyeDatosDePfB()
    {
        // arrange: buscar el día que tiene sesión (Lunes) y sembrar una asignación a atleta para PF B (#KAN-528)
        var date = _dataB.Assignment.StartDate;
        while (date.DayOfWeek != DayOfWeek.Monday)
        {
            date = date.AddDays(1);
        }

        var athleteAssignment = PlanAssignment.Create(
            _dataB.Plan.Id,
            _dataB.Assignment.StartDate,
            athleteId: _dataB.AthleteId);

        using (var pfContext = _factory.CreateDbContext(overridePfId: _pfBId))
        {
            pfContext.PlanAssignments.Add(athleteAssignment);
            await pfContext.SaveChangesAsync();
        }

        // Act 1: cliente con rol Servicio consultando con el pfId de PF A sobre SQLite real
        var serviceClient = _factory.CreateAuthenticatedClient(Guid.Empty, role: "Servicio");
        var responsePfA = await serviceClient.GetAsync(
            $"/api/v1/internal/training-sessions/athletes-with-session?date={date:yyyy-MM-dd}&pfId={_pfAId}");

        // Assert 1: con el pfId de PF A no vienen ni los atletas ni las categorías de PF B
        responsePfA.StatusCode.Should().Be(HttpStatusCode.OK);
        var targetsPfA = await responsePfA.Content.ReadFromJsonAsync<ScheduledSessionTargetsResponse>();
        targetsPfA.Should().NotBeNull();
        targetsPfA!.AthleteIds.Should().NotContain(_dataB.AthleteId);
        targetsPfA.CategoryIds.Should().NotContain(_dataB.CategoryId);

        // Act 2: consultando con PF B sí vienen tanto el atleta como la categoría
        var responsePfB = await serviceClient.GetAsync(
            $"/api/v1/internal/training-sessions/athletes-with-session?date={date:yyyy-MM-dd}&pfId={_pfBId}");
        responsePfB.StatusCode.Should().Be(HttpStatusCode.OK);
        var targetsPfB = await responsePfB.Content.ReadFromJsonAsync<ScheduledSessionTargetsResponse>();
        targetsPfB.Should().NotBeNull();
        targetsPfB!.AthleteIds.Should().Contain(_dataB.AthleteId);
        targetsPfB.CategoryIds.Should().Contain(_dataB.CategoryId);
    }
}