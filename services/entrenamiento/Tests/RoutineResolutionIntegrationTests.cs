using Application.DTOs.Memberships;
using Application.Interfaces.Services;
using Application.UseCases.Routines.GetTodayRoutine;
using Domain.Common;
using Domain.Entities;
using Domain.Services;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Entrenamiento.Tests;

public class RoutineResolutionIntegrationTests
{
    private sealed class TestCurrentUser(Guid pfId) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    private static AppDbContext CreateInMemoryDbContext(Guid pfId, string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new AppDbContext(options, new TestCurrentUser(pfId));
    }

    [Fact]
    public async Task Given_DosAtletasEnMismaCategoriaConDistinto1Rm_When_AmbosPidenRutinaAl75PorCiento_Then_Reciben75KgY60KgRespectivamente()
    {
        // 1. Arrange (Sembrado de datos en base de datos real en memoria)
        var pfId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var sharedCategoryId = Guid.NewGuid();
        var extraCategoryAthlete1Id = Guid.NewGuid(); // Demuestra pertenencia a múltiples categorías

        var athlete1Id = Guid.NewGuid();
        var athlete2Id = Guid.NewGuid();

        var exercise = Exercise.Create("Press de Banca", "Pecho");

        // Plan de entrenamiento asignado a la categoría compartida
        var plan = TrainingPlan.Create("Plan Competitivo", "Fuerza");
        var week = TrainingWeek.Create(1, plan.Id);
        var session = TrainingSession.Create(week.Id, "Día Principal", 1, ArgentinaTime.DayOfWeek, "Calentar bien hombros");

        // Prescripción al 75%
        var prescribedExercise = PrescribedExercise.Create(order: 1, sets: 4, reps: 6, targetPercentage: 75m, session.Id, exercise.Id);
        session.AddPrescribedExercise(prescribedExercise);
        week.AddSession(session);
        plan.AddWeek(week);

        // Asignación activa hoy para la categoría
        var assignment = PlanAssignment.Create(plan.Id, ArgentinaTime.Today, categoryId: sharedCategoryId);

        // Récords de 1RM: Atleta 1 (100 kg), Atleta 2 (80 kg)
        var recordAthlete1 = AthleteRecord.Create(athlete1Id, exercise.Id, 100m, RecordSource.Tested, DateTime.UtcNow);
        var recordAthlete2 = AthleteRecord.Create(athlete2Id, exercise.Id, 80m, RecordSource.Tested, DateTime.UtcNow);

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            await context.Exercises.AddAsync(exercise);
            await context.TrainingPlans.AddAsync(plan);
            await context.PlanAssignments.AddAsync(assignment);
            await context.AthleteRecords.AddRangeAsync(recordAthlete1, recordAthlete2);
            await context.SaveChangesAsync();
        }

        // 2. Mock de Estructura: simulamos que ambos atletas pertenecen a la categoría compartida
        var structureClientMock = new Mock<IStructureClient>();

        // Atleta 1 está en múltiples categorías: la compartida + otra adicional
        structureClientMock
            .Setup(c => c.GetAthleteCategoriesAsync(athlete1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AthleteCategoryDto(sharedCategoryId, Guid.NewGuid(), "Plantel Superior", null),
                new AthleteCategoryDto(extraCategoryAthlete1Id, Guid.NewGuid(), "Seven", null)
            ]);

        structureClientMock
            .Setup(c => c.GetAthleteByIdAsync(athlete1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteDto(athlete1Id, "Atleta Uno"));

        // Atleta 2 solo en la categoría compartida
        structureClientMock
            .Setup(c => c.GetAthleteCategoriesAsync(athlete2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AthleteCategoryDto(sharedCategoryId, Guid.NewGuid(), "Plantel Superior", null)
            ]);

        structureClientMock
            .Setup(c => c.GetAthleteByIdAsync(athlete2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteDto(athlete2Id, "Atleta Dos"));

        // 3. Act & Assert: Ejecutamos la consulta para ambos atletas
        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            var sessionRepo = new TrainingSessionRepository(context);
            var recordRepo = new AthleteRecordRepository(context);
            var loadCalculator = new LoadCalculatorEngine();

            var handler = new GetTodayRoutineQueryHandler(recordRepo, sessionRepo, loadCalculator, structureClientMock.Object);

            // Consulta Atleta 1
            var routineAthlete1 = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athlete1Id });
            var exercise1 = Assert.Single(routineAthlete1.Exercises);
            Assert.Equal(100.0, exercise1.Current1RmKg);
            Assert.Equal(75.0, exercise1.WeightKg);
            Assert.False(exercise1.IsReference);

            // Consulta Atleta 2
            var routineAthlete2 = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athlete2Id });
            var exercise2 = Assert.Single(routineAthlete2.Exercises);
            Assert.Equal(80.0, exercise2.Current1RmKg);
            Assert.Equal(60.0, exercise2.WeightKg);
            Assert.False(exercise2.IsReference);
        }
    }

    [Fact]
    public async Task Given_AtletasConDistintosOrigenesDe1Rm_When_PidenRutinaAl75PorCiento_Then_AplicaFactorSeguridadYMarcasDeReferencia()
    {
        var pfId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var categoryId = Guid.NewGuid();

        var athleteTestedId = Guid.NewGuid();
        var athleteEstimatedId = Guid.NewGuid();
        var athleteNoRecordId = Guid.NewGuid();

        var exercise = Exercise.Create("Sentadilla Trasera", "Piernas");
        var plan = TrainingPlan.Create("Plan Fuerza", "Fuerza");
        var week = TrainingWeek.Create(1, plan.Id);
        var session = TrainingSession.Create(week.Id, "Día 1", 1, ArgentinaTime.DayOfWeek);
        session.AddPrescribedExercise(PrescribedExercise.Create(1, 4, 6, 75m, session.Id, exercise.Id));
        week.AddSession(session);
        plan.AddWeek(week);

        var assignment = PlanAssignment.Create(plan.Id, ArgentinaTime.Today, categoryId: categoryId);

        var recordTested = AthleteRecord.Create(athleteTestedId, exercise.Id, 100m, RecordSource.Tested, DateTime.UtcNow);
        var recordEstimated = AthleteRecord.Create(athleteEstimatedId, exercise.Id, 100m, RecordSource.Estimated, DateTime.UtcNow);

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            await context.Exercises.AddAsync(exercise);
            await context.TrainingPlans.AddAsync(plan);
            await context.PlanAssignments.AddAsync(assignment);
            await context.AthleteRecords.AddRangeAsync(recordTested, recordEstimated);
            await context.SaveChangesAsync();
        }

        var structureClientMock = new Mock<IStructureClient>();
        structureClientMock.Setup(c => c.GetAthleteCategoriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AthleteCategoryDto(categoryId, Guid.NewGuid(), "Plantel Superior", null)]);

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            var handler = new GetTodayRoutineQueryHandler(
                new AthleteRecordRepository(context),
                new TrainingSessionRepository(context),
                new LoadCalculatorEngine(),
                structureClientMock.Object);

            // 1. Tested: 100 * 0.75 = 75.0 kg
            var resTested = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athleteTestedId });
            var exTested = Assert.Single(resTested.Exercises);
            Assert.Equal(100.0, exTested.Current1RmKg);
            Assert.Equal(75.0, exTested.WeightKg);
            Assert.False(exTested.IsReference);

            // 2. Estimated: (100 * 0.75) * 0.90 = 67.5 kg
            var resEstimated = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athleteEstimatedId });
            var exEstimated = Assert.Single(resEstimated.Exercises);
            Assert.Equal(100.0, exEstimated.Current1RmKg);
            Assert.Equal(67.5, exEstimated.WeightKg);
            Assert.False(exEstimated.IsReference);

            // 3. Sin récord previo: serie de referencia
            var resNoRecord = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athleteNoRecordId });
            var exNoRecord = Assert.Single(resNoRecord.Exercises);
            Assert.Null(exNoRecord.Current1RmKg);
            Assert.Null(exNoRecord.WeightKg);
            Assert.True(exNoRecord.IsReference);
        }
    }

    [Fact]
    public async Task Given_AtletaEnMultiplesCategoriasConsultandoSuPropiaRutina_When_LlamaEndpointMe_Then_ResuelvePlanDeCategoriaYCalculaCarga()
    {
        var pfId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var athleteId = Guid.NewGuid();
        var categoryPrimeraId = Guid.NewGuid();
        var categorySevenId = Guid.NewGuid();

        var exercise = Exercise.Create("Press Militar", "Hombros");
        var plan = TrainingPlan.Create("Plan General", "Fuerza");
        var week = TrainingWeek.Create(1, plan.Id);
        var session = TrainingSession.Create(week.Id, "Día Hombros", 1, ArgentinaTime.DayOfWeek, "Cuidado con la postura");
        session.AddPrescribedExercise(PrescribedExercise.Create(1, 3, 8, 80m, session.Id, exercise.Id));
        week.AddSession(session);
        plan.AddWeek(week);

        var assignment = PlanAssignment.Create(plan.Id, ArgentinaTime.Today, categoryId: categoryPrimeraId);
        var record = AthleteRecord.Create(athleteId, exercise.Id, 60m, RecordSource.Tested, DateTime.UtcNow);

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            await context.Exercises.AddAsync(exercise);
            await context.TrainingPlans.AddAsync(plan);
            await context.PlanAssignments.AddAsync(assignment);
            await context.AthleteRecords.AddAsync(record);
            await context.SaveChangesAsync();
        }

        var structureClientMock = new Mock<IStructureClient>();
        structureClientMock
            .Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteMeDto
            {
                AthleteId = athleteId,
                FirstName = "Martín",
                LastName = "Pérez",
                FullName = "Martín Pérez",
                Active = true,
                Categories = [
                    new AthleteCategoryDto(categoryPrimeraId, Guid.NewGuid(), "Primera", null),
                    new AthleteCategoryDto(categorySevenId, Guid.NewGuid(), "Seven", null)
                ]
            });

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            var handler = new GetTodayRoutineQueryHandler(
                new AthleteRecordRepository(context),
                new TrainingSessionRepository(context),
                new LoadCalculatorEngine(),
                structureClientMock.Object);

            // Consulta como Atleta (/api/v1/me/today-routine -> AthleteId = null)
            var routine = await handler.Handle(new GetTodayRoutineQuery());

            Assert.Equal(athleteId, routine.AthleteId);
            Assert.Equal("Martín Pérez", routine.AthleteName);
            Assert.Equal("Cuidado con la postura", routine.CoachNotes);

            var ex = Assert.Single(routine.Exercises);
            Assert.Equal("Press Militar", ex.Name);
            Assert.Equal(60.0, ex.Current1RmKg);
            // 60 kg * 80% = 48 kg -> redondeado a 2.5 kg = 47.5 kg
            Assert.Equal(47.5, ex.WeightKg);
            Assert.False(ex.IsReference);
        }
    }
}