using System.Reflection;
using System.Text.Json;
using Application.DTOs.Memberships;
using Application.DTOs.Routines;
using Application.DTOs.TrainingSessions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Routines.GetTodayRoutine;
using Application.UseCases.Routines.RecordPerformedSets;
using Application.UseCases.TrainingSessions.ApplyAthleteAdjustment;
using Domain.Common;
using Domain.Entities;
using Domain.Services;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Entrenamiento.Tests;

/// <summary>
/// Pruebas automatizadas para la aplicación del ajuste del día por atleta y sesión
/// y la resolución de la rutina de hoy con sustituto y 1RM del sustituto (#157 · KAN-452).
/// </summary>
public sealed class AthleteSessionAdjustmentsTests
{
    private static readonly Guid PfId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private sealed class FakeCurrentUser(Guid? pfId, Guid? userId = null, string role = "PreparadorFisico") : ICurrentUser
    {
        public Guid? UserId => userId ?? pfId;
        public Guid? PfId => pfId;
        public string? Role => role;
        public bool IsAuthenticated => true;
    }

    private static AppDbContext CreateInMemoryDb(string dbName, Guid? pfId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new AppDbContext(options, new FakeCurrentUser(pfId));
    }

    private static TrainingSession CreateSessionWithExercise(
        Exercise exercise,
        decimal targetPercentage,
        string? coachNotes = "Controlar técnica")
    {
        var session = TrainingSession.Create(Guid.NewGuid(), "Día 1", 1, DayOfWeek.Monday, coachNotes);
        var prescribed = PrescribedExercise.Create(1, 4, 6, targetPercentage, session.Id, exercise.Id);

        typeof(PrescribedExercise).GetProperty(nameof(PrescribedExercise.Exercise))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(prescribed, [exercise]);

        var list = (List<PrescribedExercise>)typeof(TrainingSession)
            .GetField("_prescribedExercises", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session)!;
        list.Add(prescribed);

        return session;
    }

    // =========================================================================
    // 1. ApplyAthleteSessionAdjustmentCommandHandler guarda y actualiza el ajuste
    // =========================================================================
    [Fact]
    public async Task Given_ValidSessionAndSubstitute_When_ApplyAthleteSessionAdjustment_Then_PersistsAdjustmentAndUpdatesIfCalledAgain()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateInMemoryDb(dbName, PfId);

        var boxJump = Exercise.Create("Salto al cajón", "Piernas");
        var legPress = Exercise.Create("Prensa de piernas", "Piernas");
        var bandSquat = Exercise.Create("Sentadillas con bandas elásticas", "Piernas");
        db.Exercises.AddRange(boxJump, legPress, bandSquat);

        var session = CreateSessionWithExercise(boxJump, 80m);
        db.TrainingSessions.Add(session);
        await db.SaveChangesAsync();

        var sessionRepo = new TrainingSessionRepository(db);
        var exerciseRepo = new ExerciseRepository(db);
        var adjustmentRepo = new AthleteSessionAdjustmentRepository(db);
        var uow = new UnitOfWork(db);

        var structureMock = new Mock<IStructureClient>();
        var athleteId = Guid.NewGuid();
        structureMock
            .Setup(c => c.GetAthleteByIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteDto(athleteId, "Lucas Fernández"));

        var handler = new ApplyAthleteSessionAdjustmentCommandHandler(
            sessionRepo,
            adjustmentRepo,
            exerciseRepo,
            structureMock.Object,
            uow);

        var today = ArgentinaTime.Today;
        var note = "Se cambia Salto al cajón por Prensa de piernas por molestia en el cuádriceps (nivel 2) reportada hoy.";

        var firstResult = await handler.Handle(new ApplyAthleteSessionAdjustmentCommand(
            session.Id,
            new ApplyAthleteSessionAdjustmentRequest(
                athleteId,
                today,
                note,
                [new AthleteSessionAdjustmentItemDto(boxJump.Id, legPress.Id)])));

        Assert.Equal(note, firstResult.Note);
        Assert.Single(firstResult.Items);
        Assert.Equal(legPress.Id, firstResult.Items[0].SubstituteExerciseId);

        // Si se vuelve a aplicar para el mismo atleta, sesión y fecha, actualiza sin duplicar
        var secondResult = await handler.Handle(new ApplyAthleteSessionAdjustmentCommand(
            session.Id,
            new ApplyAthleteSessionAdjustmentRequest(
                athleteId,
                today,
                "Se cambia por bandas elásticas.",
                [new AthleteSessionAdjustmentItemDto(boxJump.Id, bandSquat.Id)])));

        Assert.Equal(firstResult.Id, secondResult.Id);
        Assert.Equal(bandSquat.Id, secondResult.Items[0].SubstituteExerciseId);
        Assert.Equal(1, await db.AthleteSessionAdjustments.CountAsync());
    }

    // =========================================================================
    // 2. GetTodayRoutine aplica el ajuste con el 1RM del sustituto (y si no tiene 1RM, muestra solo el %)
    // =========================================================================
    [Fact]
    public async Task Given_ApprovedAdjustment_When_AthleteOpensTodayRoutine_Then_ShowsSubstituteWithCalculatedWeightAndAdjustedByCoachTrue()
    {
        var athleteId = Guid.NewGuid();
        var today = ArgentinaTime.Today;

        var boxJump = Exercise.Create("Salto al cajón", "Piernas");
        var legPress = Exercise.Create("Prensa de piernas", "Piernas", equipment: "Máquina");
        var session = CreateSessionWithExercise(boxJump, targetPercentage: 80m, coachNotes: "Calentar bien");

        var note = "Se cambia Salto al cajón por Prensa de piernas por molestia en el cuádriceps (nivel 2) reportada hoy.";
        var adjustment = AthleteSessionAdjustment.Create(
            session.Id,
            athleteId,
            today,
            JsonSerializer.Serialize(
                new[] { new AthleteSessionAdjustmentItemDto(boxJump.Id, legPress.Id) },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            note);

        var sessionRepoMock = new Mock<ITrainingSessionRepository>();
        sessionRepoMock
            .Setup(r => r.GetByAthleteIdAsync(athleteId, It.IsAny<IReadOnlyList<Guid>>(), today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var adjustmentRepoMock = new Mock<IAthleteSessionAdjustmentRepository>();
        adjustmentRepoMock
            .Setup(r => r.GetBySessionAthleteAndDateAsNoTrackingAsync(session.Id, athleteId, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adjustment);

        var exerciseRepoMock = new Mock<IExerciseRepository>();
        exerciseRepoMock
            .Setup(r => r.ListByIdsAsync(It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(legPress.Id)), It.IsAny<CancellationToken>()))
            .ReturnsAsync([legPress]);

        // Caso A: El atleta tiene 1RM testeado de 150 kg en Prensa de piernas -> 80% = 120 kg
        var recordRepoMock = new Mock<IAthleteRecordRepository>();
        var legPressRecord = AthleteRecord.Create(athleteId, legPress.Id, 150m, RecordSource.Tested, DateTime.UtcNow);
        recordRepoMock
            .Setup(r => r.GetActiveRecordAsync(athleteId, legPress.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(legPressRecord);

        var structureMock = new Mock<IStructureClient>();
        structureMock
            .Setup(c => c.GetAthleteByIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteDto(athleteId, "Lucas Fernández"));

        var videoRepoMock = new Mock<IExerciseVideoRepository>();
        videoRepoMock
            .Setup(r => r.GetVideosByExerciseIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());

        var handler = new GetTodayRoutineQueryHandler(
            recordRepoMock.Object,
            sessionRepoMock.Object,
            new LoadCalculatorEngine(),
            structureMock.Object,
            videoRepoMock.Object,
            adjustmentRepoMock.Object,
            exerciseRepoMock.Object);

        var routineWithRecord = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athleteId, Date = today });

        Assert.True(routineWithRecord.AdjustedByCoach);
        Assert.True(routineWithRecord.RoutineAdapted);
        Assert.Equal(note, routineWithRecord.AdjustmentNote);
        Assert.Contains(note, routineWithRecord.CoachNotes);
        Assert.Equal(session.Id, routineWithRecord.SessionId);

        var itemWithRecord = Assert.Single(routineWithRecord.Exercises);
        Assert.Equal(legPress.Id, itemWithRecord.ExerciseId);
        Assert.Equal("Prensa de piernas", itemWithRecord.Name);
        Assert.Equal(80.0, itemWithRecord.Percentage);
        Assert.Equal(120.0, itemWithRecord.WeightKg);
        Assert.Equal(150.0, itemWithRecord.Current1RmKg);
        Assert.False(itemWithRecord.IsReference);

        // Caso B: El atleta NO tiene 1RM cargado para el sustituto -> muestra solo el % (weightKg = null, isReference = true)
        recordRepoMock
            .Setup(r => r.GetActiveRecordAsync(athleteId, legPress.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteRecord?)null);

        var routineWithoutRecord = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athleteId, Date = today });
        var itemWithoutRecord = Assert.Single(routineWithoutRecord.Exercises);
        Assert.Equal(legPress.Id, itemWithoutRecord.ExerciseId);
        Assert.Equal("Prensa de piernas", itemWithoutRecord.Name);
        Assert.Equal(80.0, itemWithoutRecord.Percentage);
        Assert.Null(itemWithoutRecord.WeightKg);
        Assert.Null(itemWithoutRecord.Current1RmKg);
        Assert.True(itemWithoutRecord.IsReference);
    }

    // =========================================================================
    // 3. RecordPerformedSets permite registrar series del ejercicio sustituto cuando hay ajuste hoy
    // =========================================================================
    [Fact]
    public async Task Given_ApprovedAdjustmentToday_When_AthleteRecordsPerformedSetForSubstitute_Then_Succeeds()
    {
        var athleteId = Guid.NewGuid();
        var today = ArgentinaTime.Today;

        var boxJump = Exercise.Create("Salto al cajón", "Piernas");
        var legPress = Exercise.Create("Prensa de piernas", "Piernas");
        var session = CreateSessionWithExercise(boxJump, targetPercentage: 80m);

        var adjustment = AthleteSessionAdjustment.Create(
            session.Id,
            athleteId,
            today,
            JsonSerializer.Serialize(
                new[] { new AthleteSessionAdjustmentItemDto(boxJump.Id, legPress.Id) },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            "Ajustado por el PF.");

        var structureMock = new Mock<IStructureClient>();
        structureMock
            .Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteMeDto
            {
                AthleteId = athleteId,
                FirstName = "Lucas",
                LastName = "Fernández",
                FullName = "Lucas Fernández",
                Active = true,
                Categories = []
            });

        var sessionRepoMock = new Mock<ITrainingSessionRepository>();
        sessionRepoMock
            .Setup(r => r.GetByAthleteIdAsync(athleteId, It.IsAny<IReadOnlyList<Guid>>(), today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var adjustmentRepoMock = new Mock<IAthleteSessionAdjustmentRepository>();
        adjustmentRepoMock
            .Setup(r => r.GetBySessionAthleteAndDateAsNoTrackingAsync(session.Id, athleteId, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adjustment);

        var performedSetRepoMock = new Mock<IPerformedSetRepository>();
        var recordRepoMock = new Mock<IAthleteRecordRepository>();
        var proposalRepoMock = new Mock<IRecordProposalRepository>();
        var uowMock = new Mock<IUnitOfWork>();

        var handler = new RecordPerformedSetsCommandHandler(
            structureMock.Object,
            sessionRepoMock.Object,
            recordRepoMock.Object,
            performedSetRepoMock.Object,
            proposalRepoMock.Object,
            uowMock.Object,
            adjustmentRepoMock.Object);

        var response = await handler.Handle(new RecordPerformedSetsCommand(
        [
            new PerformedSetInputDto
            {
                ExerciseId = legPress.Id,
                Weight = 120m,
                Reps = 6
            }
        ]));

        Assert.Equal(1, response.RecordedSetsCount);
        performedSetRepoMock.Verify(
            r => r.AddRangeAsync(
                It.Is<IEnumerable<PerformedSet>>(sets => sets.Any(ps => ps.ExerciseId == legPress.Id && ps.Weight == 120m)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
