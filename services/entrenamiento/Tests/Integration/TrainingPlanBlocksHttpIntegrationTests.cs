using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.DTOs.TrainingPlans;
using Domain.Entities;
using Entrenamiento.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Entrenamiento.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class TrainingPlanBlocksHttpIntegrationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly Guid _pfId = Guid.NewGuid();
    private EntrenamientoTestApplicationFactory _factory = null!;
    private HttpClient _clientPf = null!;
    private PfSeedData _seedData = null!;

    public async Task InitializeAsync()
    {
        _factory = new EntrenamientoTestApplicationFactory();
        _clientPf = _factory.CreateAuthenticatedClient(_pfId);
        _seedData = await _factory.SeedPfDataAsync(_pfId);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task PostPlanConBloques_SeguidoDeGet_RetornaEstructuraDeBloquesYMétricas()
    {
        // Arrange
        var catalogEx = _seedData.CatalogExercise;
        var customEx = _seedData.CustomExercise;

        var request = new SaveTrainingPlanRequest(
            Name: "Plan Periodizado con Bloques",
            Objective: "Fuerza e Hipertrofia",
            Weeks:
            [
                new TrainingWeekRequest(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new TrainingSessionRequest(
                            Name: "Día 1 - Torso",
                            Order: 1,
                            DayOfWeek: DayOfWeek.Monday,
                            CoachNotes: "Priorizar técnica y RPE",
                            Blocks:
                            [
                                new TrainingBlockRequest(
                                    Title: "Entrada en calor",
                                    Order: 1,
                                    Exercises:
                                    [
                                        new PrescribedExerciseRequest(
                                            exerciseId: catalogEx.Id,
                                            order: 1,
                                            sets: 2,
                                            reps: "10-12",
                                            targetPercentage: null,
                                            intensityType: "RIR",
                                            targetRir: 3,
                                            notes: "Movilidad articular"
                                        )
                                    ]
                                ),
                                new TrainingBlockRequest(
                                    Title: "Fuerza Principal",
                                    Order: 2,
                                    Exercises:
                                    [
                                        new PrescribedExerciseRequest(
                                            exerciseId: customEx.Id,
                                            order: 1,
                                            sets: 4,
                                            reps: "6-8",
                                            targetPercentage: 80.0m,
                                            intensityType: "RPE",
                                            targetRpe: 8.5m,
                                            notes: "Pausa en el fondo"
                                        )
                                    ]
                                )
                            ]
                        )
                    ]
                )
            ]
        );

        // Act 1: POST por HTTP
        var postResponse = await _clientPf.PostAsJsonAsync("/api/v1/training-plans", request);

        // Assert 1: Created
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var createdPlan = await postResponse.Content.ReadFromJsonAsync<TrainingPlanDetailDto>(JsonOptions);
        Assert.NotNull(createdPlan);
        Assert.NotEqual(Guid.Empty, createdPlan!.Id);

        // Act 2: GET por HTTP
        var getResponse = await _clientPf.GetAsync($"/api/v1/training-plans/{createdPlan.Id}");

        // Assert 2: OK y validación exhaustiva de bloques y valores
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var planDetail = await getResponse.Content.ReadFromJsonAsync<TrainingPlanDetailDto>(JsonOptions);
        Assert.NotNull(planDetail);
        Assert.Equal("Plan Periodizado con Bloques", planDetail!.Name);
        Assert.Single(planDetail.Weeks);

        var week = planDetail.Weeks[0];
        Assert.Equal(1, week.WeekNumber);
        Assert.Single(week.Sessions);

        var session = week.Sessions[0];
        Assert.Equal("Día 1 - Torso", session.Name);
        Assert.Equal(DayOfWeek.Monday, session.DayOfWeek);
        Assert.Equal(2, session.Blocks.Count);

        // Bloque 1
        var block1 = session.Blocks[0];
        Assert.Equal("Entrada en calor", block1.Title);
        Assert.Equal(1, block1.Order);
        Assert.Single(block1.Exercises);
        var ex1 = block1.Exercises[0];
        Assert.Equal(catalogEx.Id, ex1.ExerciseId);
        Assert.Equal("10-12", ex1.Reps);
        Assert.Equal("RIR", ex1.IntensityType);
        Assert.Equal(3, ex1.TargetRir);
        Assert.Equal("Movilidad articular", ex1.Notes);

        // Bloque 2
        var block2 = session.Blocks[1];
        Assert.Equal("Fuerza Principal", block2.Title);
        Assert.Equal(2, block2.Order);
        Assert.Single(block2.Exercises);
        var ex2 = block2.Exercises[0];
        Assert.Equal(customEx.Id, ex2.ExerciseId);
        Assert.Equal("6-8", ex2.Reps);
        Assert.Equal(80.0m, ex2.TargetPercentage);
        Assert.Equal("RPE", ex2.IntensityType);
        Assert.Equal(8.5m, ex2.TargetRpe);
        Assert.Equal("Pausa en el fondo", ex2.Notes);
    }

    [Fact]
    public async Task PutPlanConBloques_SeguidoDeGet_PersisteCambiosYConservaIds()
    {
        // Arrange: Creamos un plan inicial con 1 bloque
        var catalogEx = _seedData.CatalogExercise;
        var initialRequest = new SaveTrainingPlanRequest(
            Name: "Plan Inicial para Modificar",
            Objective: "Test PUT",
            Weeks:
            [
                new TrainingWeekRequest(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new TrainingSessionRequest(
                            Name: "Sesión 1",
                            Order: 1,
                            DayOfWeek: DayOfWeek.Wednesday,
                            Blocks:
                            [
                                new TrainingBlockRequest(
                                    Title: "Bloque Inicial",
                                    Order: 1,
                                    Exercises:
                                    [
                                        new PrescribedExerciseRequest(
                                            exerciseId: catalogEx.Id,
                                            order: 1,
                                            sets: 3,
                                            reps: "10",
                                            targetPercentage: 70m
                                        )
                                    ]
                                )
                            ]
                        )
                    ]
                )
            ]
        );

        var postResponse = await _clientPf.PostAsJsonAsync("/api/v1/training-plans", initialRequest);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var planCreado = await postResponse.Content.ReadFromJsonAsync<TrainingPlanDetailDto>(JsonOptions);
        Assert.NotNull(planCreado);

        var blockId = planCreado!.Weeks[0].Sessions[0].Blocks[0].Id;
        var exerciseId = planCreado.Weeks[0].Sessions[0].Blocks[0].Exercises[0].Id;

        // Act 1: PUT modificando el bloque existente y agregando un segundo bloque
        var updateRequest = new SaveTrainingPlanRequest(
            Name: "Plan Actualizado",
            Objective: "Objetivo modificado",
            Weeks:
            [
                new TrainingWeekRequest(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new TrainingSessionRequest(
                            Name: "Sesión 1 Modificada",
                            Order: 1,
                            DayOfWeek: DayOfWeek.Wednesday,
                            Blocks:
                            [
                                new TrainingBlockRequest(
                                    Title: "Bloque 1 Modificado",
                                    Order: 1,
                                    Id: blockId,
                                    Exercises:
                                    [
                                        new PrescribedExerciseRequest(
                                            exerciseId: catalogEx.Id,
                                            order: 1,
                                            sets: 4,
                                            reps: "8-10",
                                            targetPercentage: 75m,
                                            id: exerciseId,
                                            intensityType: "RPE",
                                            targetRpe: 8m,
                                            notes: "Actualizado con RPE"
                                        )
                                    ]
                                ),
                                new TrainingBlockRequest(
                                    Title: "Bloque 2 Nuevo - Accesorios",
                                    Order: 2,
                                    Exercises:
                                    [
                                        new PrescribedExerciseRequest(
                                            exerciseId: _seedData.CustomExercise.Id,
                                            order: 1,
                                            sets: 3,
                                            reps: "12-15",
                                            intensityType: "RIR",
                                            targetRir: 1,
                                            notes: "Al fallo técnico"
                                        )
                                    ]
                                )
                            ]
                        )
                    ]
                )
            ]
        );

        var putResponse = await _clientPf.PutAsJsonAsync($"/api/v1/training-plans/{planCreado.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        // Act 2: GET por HTTP para comprobar persistencia
        var getResponse = await _clientPf.GetAsync($"/api/v1/training-plans/{planCreado.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var planModificado = await getResponse.Content.ReadFromJsonAsync<TrainingPlanDetailDto>(JsonOptions);
        Assert.NotNull(planModificado);
        Assert.Equal("Plan Actualizado", planModificado!.Name);

        var sessionMod = planModificado.Weeks[0].Sessions[0];
        Assert.Equal(2, sessionMod.Blocks.Count);

        // Bloque 1 conserva ID
        Assert.Equal(blockId, sessionMod.Blocks[0].Id);
        Assert.Equal("Bloque 1 Modificado", sessionMod.Blocks[0].Title);
        Assert.Equal("8-10", sessionMod.Blocks[0].Exercises[0].Reps);
        Assert.Equal("RPE", sessionMod.Blocks[0].Exercises[0].IntensityType);
        Assert.Equal(8m, sessionMod.Blocks[0].Exercises[0].TargetRpe);

        // Bloque 2 nuevo
        Assert.Equal("Bloque 2 Nuevo - Accesorios", sessionMod.Blocks[1].Title);
        Assert.Equal("12-15", sessionMod.Blocks[1].Exercises[0].Reps);
        Assert.Equal("RIR", sessionMod.Blocks[1].Exercises[0].IntensityType);
        Assert.Equal(1, sessionMod.Blocks[1].Exercises[0].TargetRir);
    }
}
