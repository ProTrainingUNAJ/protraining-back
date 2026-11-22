using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.DTOs.Memberships;
using Application.DTOs.Routines;
using Application.DTOs.TrainingPlans;
using Application.DTOs.TrainingSessions;
using Application.Interfaces.Services;
using Application.UseCases.Routines.RecordPerformedSets;
using Domain.Common;
using Domain.Entities;
using Entrenamiento.Tests.Infrastructure;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Moq;
using Xunit;

namespace Entrenamiento.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class TodayRoutineBlocksHttpIntegrationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
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
    public async Task Given_AtletaConPlanPorBloquesAsignado_When_PideRutinaEnSemana2_Then_RetornaBloquesDeSemana2ConPesoResuelto()
    {
        // Arrange: creamos un plan de 2 semanas con bloques distintos entre semana 1 y semana 2
        var warmupExercise = _seedData.CatalogExercise;
        var mainExercise = _seedData.CustomExercise;

        var startDate = new DateOnly(2026, 11, 2); // Lunes -> Semana 1
        var week2Monday = startDate.AddDays(7);    // Lunes 2026-11-09 -> Semana 2

        var planRequest = new SaveTrainingPlanRequest(
            Name: "Plan Fuerza 2 Semanas",
            Objective: "Progresión por bloques",
            Weeks:
            [
                new TrainingWeekRequest(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new TrainingSessionRequest(
                            Name: "Lunes Semana 1",
                            Order: 1,
                            DayOfWeek: DayOfWeek.Monday,
                            CoachNotes: "Semana 1 adaptación",
                            Blocks:
                            [
                                new TrainingBlockRequest(
                                    Title: "Bloque S1 - Activación",
                                    Order: 1,
                                    Exercises:
                                    [
                                        new PrescribedExerciseRequest(
                                            exerciseId: warmupExercise.Id,
                                            order: 1,
                                            sets: 2,
                                            reps: "12",
                                            targetPercentage: 50m)
                                    ]),
                                new TrainingBlockRequest(
                                    Title: "Bloque S1 - Fuerza",
                                    Order: 2,
                                    Exercises:
                                    [
                                        new PrescribedExerciseRequest(
                                            exerciseId: mainExercise.Id,
                                            order: 1,
                                            sets: 3,
                                            reps: "8",
                                            targetPercentage: 70m)
                                    ])
                            ])
                    ]),
                new TrainingWeekRequest(
                    WeekNumber: 2,
                    Sessions:
                    [
                        new TrainingSessionRequest(
                            Name: "Lunes Semana 2",
                            Order: 1,
                            DayOfWeek: DayOfWeek.Monday,
                            CoachNotes: "Semana 2 carga principal",
                            Blocks:
                            [
                                new TrainingBlockRequest(
                                    Title: "Activación",
                                    Order: 1,
                                    Exercises:
                                    [
                                        new PrescribedExerciseRequest(
                                            exerciseId: warmupExercise.Id,
                                            order: 1,
                                            sets: 2,
                                            reps: "10-12",
                                            targetPercentage: 60m)
                                    ]),
                                new TrainingBlockRequest(
                                    Title: "Fuerza Principal",
                                    Order: 2,
                                    Exercises:
                                    [
                                        new PrescribedExerciseRequest(
                                            exerciseId: mainExercise.Id,
                                            order: 1,
                                            sets: 4,
                                            reps: "6-8",
                                            targetPercentage: 80m)
                                    ])
                            ])
                    ])
            ]);

        var createPlanResponse = await _clientPf.PostAsJsonAsync("/api/v1/training-plans", planRequest);
        Assert.Equal(HttpStatusCode.Created, createPlanResponse.StatusCode);
        var createdPlan = await createPlanResponse.Content.ReadFromJsonAsync<TrainingPlanDetailDto>(JsonOptions);
        Assert.NotNull(createdPlan);

        // Asignamos el plan directamente al atleta (tiene prioridad sobre asignaciones de categoría)
        var assignResponse = await _clientPf.PostAsJsonAsync(
            "/api/v1/training-plans/assignments",
            new AssignPlanRequest(createdPlan!.Id, startDate, AthleteId: _seedData.AthleteId));
        Assert.Equal(HttpStatusCode.Created, assignResponse.StatusCode);

        // Récords 1RM:
        // - warmupExercise ya tiene 100 kg del seed -> 60% = 60.0 kg
        // - mainExercise: 110 kg * 80% = 88.0 kg -> redondeado a múltiplo de 2.5 kg = 87.5 kg
        using (var pfContext = _factory.CreateDbContext(_pfId))
        {
            pfContext.AthleteRecords.Add(
                AthleteRecord.Create(_seedData.AthleteId, mainExercise.Id, 110m, RecordSource.Tested, DateTime.UtcNow));
            await pfContext.SaveChangesAsync();
        }

        // Act: consultamos la rutina en un día perteneciente a la semana 2
        var response = await _clientPf.GetAsync(
            $"/api/v1/athletes/{_seedData.AthleteId}/today-routine?date={week2Monday:yyyy-MM-dd}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var routine = await response.Content.ReadFromJsonAsync<TodayRoutineResponseDto>(JsonOptions);
        Assert.NotNull(routine);

        Assert.Equal(week2Monday, routine!.Date);
        Assert.Equal("Semana 2 carga principal", routine.CoachNotes);
        Assert.Equal(2, routine.Blocks.Count);

        var block1 = routine.Blocks[0];
        Assert.Equal("Activación", block1.Title);
        Assert.Equal(1, block1.Order);
        var ex1 = Assert.Single(block1.Exercises);
        Assert.Equal(warmupExercise.Id, ex1.ExerciseId);
        Assert.Equal(2, ex1.Sets);
        Assert.Equal("10-12", ex1.RepsRange);
        Assert.Equal(60.0, ex1.Percentage);
        Assert.Equal(100.0, ex1.Current1RmKg);
        Assert.Equal(60.0, ex1.WeightKg);

        var block2 = routine.Blocks[1];
        Assert.Equal("Fuerza Principal", block2.Title);
        Assert.Equal(2, block2.Order);
        var ex2 = Assert.Single(block2.Exercises);
        Assert.Equal(mainExercise.Id, ex2.ExerciseId);
        Assert.Equal(4, ex2.Sets);
        Assert.Equal(6, ex2.Reps);
        Assert.Equal("6-8", ex2.RepsRange);
        Assert.Equal(80.0, ex2.Percentage);
        Assert.Equal(110.0, ex2.Current1RmKg);
        Assert.Equal(87.5, ex2.WeightKg);

        // Compatibilidad con lista plana ordenada
        Assert.Equal(2, routine.Exercises.Count);
        Assert.Equal(ex1.ExerciseId, routine.Exercises[0].ExerciseId);
        Assert.Equal(ex2.ExerciseId, routine.Exercises[1].ExerciseId);
    }

    [Fact]
    public async Task Given_AjusteDelDiaAprobado_When_AtletaPideRutinaPorBloques_Then_VeSustitutoEnElMismoBloqueConPesoResuelto()
    {
        // Arrange: plan con 2 bloques en el día objetivo y un ejercicio sustituto para aplicar en el ajuste
        var warmupExercise = _seedData.CatalogExercise;
        var originalMainExercise = _seedData.CustomExercise;

        var accessoryExercise = Exercise.Create("Estocadas Búlgaras", "Piernas");
        var substituteExercise = Exercise.Create("Prensa de Piernas", "Piernas");

        using (var pfContext = _factory.CreateDbContext(_pfId))
        {
            pfContext.Exercises.AddRange(accessoryExercise, substituteExercise);
            pfContext.AthleteRecords.AddRange(
                AthleteRecord.Create(_seedData.AthleteId, originalMainExercise.Id, 100m, RecordSource.Tested, DateTime.UtcNow),
                AthleteRecord.Create(_seedData.AthleteId, substituteExercise.Id, 150m, RecordSource.Tested, DateTime.UtcNow));
            await pfContext.SaveChangesAsync();
        }

        var targetDate = new DateOnly(2026, 11, 11); // Miércoles

        var planRequest = new SaveTrainingPlanRequest(
            Name: "Plan con Ajuste en Bloque",
            Objective: "Fuerza",
            Weeks:
            [
                new TrainingWeekRequest(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new TrainingSessionRequest(
                            Name: "Miércoles Piernas",
                            Order: 1,
                            DayOfWeek: DayOfWeek.Wednesday,
                            Blocks:
                            [
                                new TrainingBlockRequest(
                                    Title: "Entrada en calor",
                                    Order: 1,
                                    Exercises:
                                    [
                                        new PrescribedExerciseRequest(
                                            exerciseId: warmupExercise.Id,
                                            order: 1,
                                            sets: 2,
                                            reps: "10",
                                            targetPercentage: 50m)
                                    ]),
                                new TrainingBlockRequest(
                                    Title: "Fuerza Principal",
                                    Order: 2,
                                    Exercises:
                                    [
                                        new PrescribedExerciseRequest(
                                            exerciseId: originalMainExercise.Id,
                                            order: 1,
                                            sets: 4,
                                            reps: "6",
                                            targetPercentage: 80m),
                                        new PrescribedExerciseRequest(
                                            exerciseId: accessoryExercise.Id,
                                            order: 2,
                                            sets: 3,
                                            reps: "10",
                                            targetPercentage: null)
                                    ])
                            ])
                    ])
            ]);

        var createPlanResponse = await _clientPf.PostAsJsonAsync("/api/v1/training-plans", planRequest);
        Assert.Equal(HttpStatusCode.Created, createPlanResponse.StatusCode);
        var createdPlan = await createPlanResponse.Content.ReadFromJsonAsync<TrainingPlanDetailDto>(JsonOptions);
        Assert.NotNull(createdPlan);

        var sessionId = createdPlan!.Weeks[0].Sessions[0].Id;

        var assignResponse = await _clientPf.PostAsJsonAsync(
            "/api/v1/training-plans/assignments",
            new AssignPlanRequest(createdPlan.Id, targetDate, AthleteId: _seedData.AthleteId));
        Assert.Equal(HttpStatusCode.Created, assignResponse.StatusCode);

        // Aplicamos ajuste aprobado del PF sustituyendo el ejercicio principal del bloque 2
        var adjustmentRequest = new ApplyAthleteSessionAdjustmentRequest(
            AthleteId: _seedData.AthleteId,
            Date: targetDate,
            Note: "Molestia leve en rodilla izquierda",
            Items: [new AthleteSessionAdjustmentItemDto(originalMainExercise.Id, substituteExercise.Id)],
            TrainingPlanId: createdPlan.Id);

        var adjustmentResponse = await _clientPf.PostAsJsonAsync(
            $"/api/v1/training-sessions/{sessionId}/athlete-adjustments",
            adjustmentRequest);
        Assert.Equal(HttpStatusCode.OK, adjustmentResponse.StatusCode);

        // Act
        var routineResponse = await _clientPf.GetAsync(
            $"/api/v1/athletes/{_seedData.AthleteId}/today-routine?date={targetDate:yyyy-MM-dd}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, routineResponse.StatusCode);
        var routine = await routineResponse.Content.ReadFromJsonAsync<TodayRoutineResponseDto>(JsonOptions);
        Assert.NotNull(routine);

        Assert.True(routine!.AdjustedByCoach);
        Assert.True(routine.RoutineAdapted);
        Assert.Equal("Molestia leve en rodilla izquierda", routine.AdjustmentNote);
        Assert.Equal(2, routine.Blocks.Count);

        // Bloque 1 intacto
        var block1 = routine.Blocks[0];
        Assert.Equal("Entrada en calor", block1.Title);
        var b1Ex = Assert.Single(block1.Exercises);
        Assert.Equal(warmupExercise.Id, b1Ex.ExerciseId);
        Assert.False(b1Ex.IsAdapted);

        // Bloque 2 contiene al sustituto en la misma posición, con el 1RM del sustituto (150 * 80% = 120 kg)
        var block2 = routine.Blocks[1];
        Assert.Equal("Fuerza Principal", block2.Title);
        Assert.Equal(2, block2.Exercises.Count);

        var substituted = block2.Exercises[0];
        Assert.Equal(substituteExercise.Id, substituted.ExerciseId);
        Assert.Equal("Prensa de Piernas", substituted.Name);
        Assert.True(substituted.IsAdapted);
        Assert.Equal(originalMainExercise.Name, substituted.OriginalExerciseName);
        Assert.Equal("Molestia leve en rodilla izquierda", substituted.AdaptationReason);
        Assert.Equal(150.0, substituted.Current1RmKg);
        Assert.Equal(120.0, substituted.WeightKg);

        var unchangedAccessory = block2.Exercises[1];
        Assert.Equal(accessoryExercise.Id, unchangedAccessory.ExerciseId);
        Assert.False(unchangedAccessory.IsAdapted);
        Assert.Null(unchangedAccessory.WeightKg);
        Assert.False(unchangedAccessory.IsReference);
        Assert.Equal(0.0, unchangedAccessory.Percentage);
    }

    [Fact]
    public async Task Given_DiaDeDescansoSinSesion_When_AtletaPideRutina_Then_Retorna200ConBloquesYEjerciciosVacios()
    {
        // El seed solo tiene sesión los Lunes; consultamos un Jueves
        var restDay = new DateOnly(2026, 11, 12);

        var response = await _clientPf.GetAsync(
            $"/api/v1/athletes/{_seedData.AthleteId}/today-routine?date={restDay:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var routine = await response.Content.ReadFromJsonAsync<TodayRoutineResponseDto>(JsonOptions);
        Assert.NotNull(routine);
        Assert.Null(routine!.SessionId);
        Assert.Empty(routine.Blocks);
        Assert.Empty(routine.Exercises);
    }

    [Fact]
    public async Task Given_DiaConDosSesiones_When_AtletaRegistraSerie_Then_NoReescribeElPlanEnBaseDeDatos()
    {
        var today = ArgentinaTime.Today;
        var exercise1 = _seedData.CatalogExercise;
        var exercise2 = _seedData.CustomExercise;

        var planRequest = new SaveTrainingPlanRequest(
            Name: "Plan Doble Sesión",
            Objective: "Integridad",
            Weeks:
            [
                new TrainingWeekRequest(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new TrainingSessionRequest(
                            Name: "Sesión Mañana",
                            Order: 1,
                            DayOfWeek: today.DayOfWeek,
                            Blocks:
                            [
                                new TrainingBlockRequest(
                                    Title: "Entrada en calor",
                                    Order: 1,
                                    Exercises:
                                    [
                                        new PrescribedExerciseRequest(
                                            exerciseId: exercise1.Id,
                                            order: 1,
                                            sets: 2,
                                            reps: "10",
                                            targetPercentage: 50m)
                                    ])
                            ]),
                        new TrainingSessionRequest(
                            Name: "Sesión Tarde",
                            Order: 2,
                            DayOfWeek: today.DayOfWeek,
                            Blocks:
                            [
                                new TrainingBlockRequest(
                                    Title: "Fuerza Principal",
                                    Order: 1,
                                    Exercises:
                                    [
                                        new PrescribedExerciseRequest(
                                            exerciseId: exercise2.Id,
                                            order: 1,
                                            sets: 4,
                                            reps: "6",
                                            targetPercentage: 80m)
                                    ])
                            ])
                    ])
            ]);

        var createPlanResponse = await _clientPf.PostAsJsonAsync("/api/v1/training-plans", planRequest);
        Assert.Equal(HttpStatusCode.Created, createPlanResponse.StatusCode);
        var createdPlan = await createPlanResponse.Content.ReadFromJsonAsync<TrainingPlanDetailDto>(JsonOptions);
        Assert.NotNull(createdPlan);

        var assignResponse = await _clientPf.PostAsJsonAsync(
            "/api/v1/training-plans/assignments",
            new AssignPlanRequest(createdPlan!.Id, today, AthleteId: _seedData.AthleteId));
        Assert.Equal(HttpStatusCode.Created, assignResponse.StatusCode);

        var structureMock = new Mock<IStructureClient>();
        structureMock
            .Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteMeDto
            {
                AthleteId = _seedData.AthleteId,
                FirstName = "Test",
                LastName = "Atleta",
                FullName = "Test Atleta",
                Active = true,
                Categories = []
            });

        // Dispara GetByAthleteIdAsync + SaveChangesAsync al registrar la serie
        using (var context = _factory.CreateDbContext(_pfId))
        {
            var recordSetsHandler = new RecordPerformedSetsCommandHandler(
                structureMock.Object,
                new TrainingSessionRepository(context),
                new AthleteRecordRepository(context),
                new PerformedSetRepository(context),
                new RecordProposalRepository(context),
                new UnitOfWork(context));

            var result = await recordSetsHandler.Handle(new RecordPerformedSetsCommand(
                [new PerformedSetInputDto { ExerciseId = exercise2.Id, Weight = 80m, Reps = 6, SetNumber = 1 }]));

            Assert.Equal(1, result.RecordedSetsCount);
        }

        // Volvemos a leer el plan y confirmamos que ambas sesiones y sus bloques siguen intactos
        var getPlanResponse = await _clientPf.GetAsync($"/api/v1/training-plans/{createdPlan.Id}");
        Assert.Equal(HttpStatusCode.OK, getPlanResponse.StatusCode);
        var persistedPlan = await getPlanResponse.Content.ReadFromJsonAsync<TrainingPlanDetailDto>(JsonOptions);
        Assert.NotNull(persistedPlan);

        var sessions = persistedPlan!.Weeks.Single().Sessions.OrderBy(s => s.Order).ToList();
        Assert.Equal(2, sessions.Count);

        var session1Block = Assert.Single(sessions[0].Blocks);
        Assert.Equal("Entrada en calor", session1Block.Title);
        Assert.Equal(1, session1Block.Order);
        Assert.Equal(exercise1.Id, Assert.Single(session1Block.Exercises).ExerciseId);

        var session2Block = Assert.Single(sessions[1].Blocks);
        Assert.Equal("Fuerza Principal", session2Block.Title);
        Assert.Equal(1, session2Block.Order);
        Assert.Equal(exercise2.Id, Assert.Single(session2Block.Exercises).ExerciseId);
    }
}
