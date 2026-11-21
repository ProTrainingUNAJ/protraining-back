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

            var handler = new GetTodayRoutineQueryHandler(recordRepo, sessionRepo, loadCalculator, structureClientMock.Object, new ExerciseVideoRepository(context), new PerformedSetRepository(context));

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

        structureClientMock.Setup(c => c.GetAthleteByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new StructureAthleteDto(id, "Lucas Gómez"));

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            var handler = new GetTodayRoutineQueryHandler(
                new AthleteRecordRepository(context),
                new TrainingSessionRepository(context),
                new LoadCalculatorEngine(),
                structureClientMock.Object,
                new ExerciseVideoRepository(context),
                new PerformedSetRepository(context));

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
                structureClientMock.Object,
                new ExerciseVideoRepository(context),
                new PerformedSetRepository(context));

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

    [Fact]
    public async Task AthleteVariant_DoesNotAffectTeammatesInSameSquad()
    {
        // CUANDO se analizan los payloads devueltos ENTONCES la variante de Atleta 1 no afectó la rutina de Atleta 2
        var pfId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var squadCategoryId = Guid.NewGuid();

        var athlete1Id = Guid.NewGuid();
        var athlete2Id = Guid.NewGuid();

        var baseExercise = Exercise.Create("Sentadilla", "Piernas");
        var substituteExercise = Exercise.Create("Prensa de piernas", "Piernas");

        var plan = TrainingPlan.Create("Plan Plantel", "Fuerza");
        var week = TrainingWeek.Create(1, plan.Id);
        var session = TrainingSession.Create(week.Id, "Día Piernas", 1, ArgentinaTime.DayOfWeek, "Controlar profundidad");
        session.AddPrescribedExercise(PrescribedExercise.Create(1, 4, 6, 80m, session.Id, baseExercise.Id));
        week.AddSession(session);
        plan.AddWeek(week);

        var assignment = PlanAssignment.Create(plan.Id, ArgentinaTime.Today, categoryId: squadCategoryId);

        // Solo Atleta 1 tiene variante activa aprobada (Sentadilla -> Prensa de piernas)
        var variantAthlete1 = AthleteVariant.Create(
            athlete1Id,
            baseExercise.Id,
            substituteExercise.Id,
            isApproved: true,
            isActive: true,
            validFrom: ArgentinaTime.Today.AddDays(-5),
            note: "Variante aprobada por molestia de rodilla");

        // Récords: Atleta 1 (150 kg en Prensa -> 80% = 120 kg), Atleta 2 (100 kg en Sentadilla -> 80% = 80 kg)
        var recordAthlete1 = AthleteRecord.Create(athlete1Id, substituteExercise.Id, 150m, RecordSource.Tested, DateTime.UtcNow);
        var recordAthlete2 = AthleteRecord.Create(athlete2Id, baseExercise.Id, 100m, RecordSource.Tested, DateTime.UtcNow);

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            await context.Exercises.AddRangeAsync(baseExercise, substituteExercise);
            await context.TrainingPlans.AddAsync(plan);
            await context.PlanAssignments.AddAsync(assignment);
            await context.AthleteVariants.AddAsync(variantAthlete1);
            await context.AthleteRecords.AddRangeAsync(recordAthlete1, recordAthlete2);
            await context.SaveChangesAsync();
        }

        var structureClientMock = new Mock<IStructureClient>();
        structureClientMock
            .Setup(c => c.GetAthleteCategoriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AthleteCategoryDto(squadCategoryId, Guid.NewGuid(), "Plantel Superior", null)]);
        structureClientMock
            .Setup(c => c.GetAthleteByIdAsync(athlete1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteDto(athlete1Id, "Lucas"));
        structureClientMock
            .Setup(c => c.GetAthleteByIdAsync(athlete2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteDto(athlete2Id, "Mateo"));

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            var handler = new GetTodayRoutineQueryHandler(
                new AthleteRecordRepository(context),
                new TrainingSessionRepository(context),
                new LoadCalculatorEngine(),
                structureClientMock.Object,
                new ExerciseVideoRepository(context),
                new PerformedSetRepository(context),
                adjustmentRepository: new AthleteSessionAdjustmentRepository(context),
                exerciseRepository: new ExerciseRepository(context),
                athleteVariantRepository: new AthleteVariantRepository(context));

            var payloadAthlete1 = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athlete1Id });
            var payloadAthlete2 = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athlete2Id });

            // Atleta 1 ve su sustituto y su carga calculada
            Assert.True(payloadAthlete1.RoutineAdapted);
            Assert.False(payloadAthlete1.AdjustedByCoach);
            var ex1 = Assert.Single(payloadAthlete1.Exercises);
            Assert.Equal(substituteExercise.Id, ex1.ExerciseId);
            Assert.Equal("Prensa de piernas", ex1.Name);
            Assert.Equal(150.0, ex1.Current1RmKg);
            Assert.Equal(120.0, ex1.WeightKg);
            Assert.True(ex1.IsAdapted);
            Assert.Equal("Sentadilla", ex1.OriginalExerciseName);
            Assert.Equal("Variante aprobada por molestia de rodilla", ex1.AdaptationReason);

            // Atleta 2 ve intacta la rutina base del plantel
            Assert.False(payloadAthlete2.RoutineAdapted);
            Assert.False(payloadAthlete2.AdjustedByCoach);
            var ex2 = Assert.Single(payloadAthlete2.Exercises);
            Assert.Equal(baseExercise.Id, ex2.ExerciseId);
            Assert.Equal("Sentadilla", ex2.Name);
            Assert.Equal(100.0, ex2.Current1RmKg);
            Assert.Equal(80.0, ex2.WeightKg);
            Assert.False(ex2.IsAdapted);
            Assert.Null(ex2.OriginalExerciseName);
            Assert.Null(ex2.AdaptationReason);
        }
    }

    [Fact]
    public async Task Given_VariantePorAtletaEnPlanPorBloques_When_ConsultaRutinaEnSemana2_Then_AplicaSustitutoDentroDeSuBloque()
    {
        var pfId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var categoryId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();

        var startDate = new DateOnly(2026, 11, 2); // Lunes -> Semana 1
        var week2Date = startDate.AddDays(7);     // Lunes -> Semana 2

        var warmup = Exercise.Create("Movilidad de Cadera", "Piernas");
        var squat = Exercise.Create("Sentadilla", "Piernas");
        var legPress = Exercise.Create("Prensa de piernas", "Piernas");

        var plan = TrainingPlan.Create("Plan Por Bloques", "Fuerza");

        var week1 = TrainingWeek.Create(1, plan.Id);
        var sessionW1 = TrainingSession.Create(week1.Id, "Día 1 S1", 1, week2Date.DayOfWeek);
        var blockW1 = TrainingBlock.Create(sessionW1.Id, "Bloque S1", 1);
        blockW1.AddPrescribedExercise(PrescribedExercise.Create(1, 3, 10, 65m, blockW1.Id, squat.Id, trainingSessionId: sessionW1.Id));
        sessionW1.AddBlock(blockW1);
        week1.AddSession(sessionW1);
        plan.AddWeek(week1);

        var week2 = TrainingWeek.Create(2, plan.Id);
        var sessionW2 = TrainingSession.Create(week2.Id, "Día 1 S2", 1, week2Date.DayOfWeek);
        var block1W2 = TrainingBlock.Create(sessionW2.Id, "Entrada en calor", 1);
        block1W2.AddPrescribedExercise(PrescribedExercise.Create(1, 2, 12, null, block1W2.Id, warmup.Id, trainingSessionId: sessionW2.Id));
        var block2W2 = TrainingBlock.Create(sessionW2.Id, "Fuerza Principal", 2);
        block2W2.AddPrescribedExercise(PrescribedExercise.Create(1, 4, "6-8", 80m, block2W2.Id, squat.Id, trainingSessionId: sessionW2.Id));
        sessionW2.AddBlock(block1W2);
        sessionW2.AddBlock(block2W2);
        week2.AddSession(sessionW2);
        plan.AddWeek(week2);

        var assignment = PlanAssignment.Create(plan.Id, startDate, categoryId: categoryId);
        var variant = AthleteVariant.Create(
            athleteId,
            squat.Id,
            legPress.Id,
            isApproved: true,
            isActive: true,
            validFrom: startDate,
            note: "Variante aprobada por molestia de rodilla");

        // 1RM 135 kg * 80% = 108 kg -> redondeado a múltiplo de 2.5 kg = 107.5 kg
        var substituteRecord = AthleteRecord.Create(athleteId, legPress.Id, 135m, RecordSource.Tested, DateTime.UtcNow);

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            await context.Exercises.AddRangeAsync(warmup, squat, legPress);
            await context.TrainingPlans.AddAsync(plan);
            await context.PlanAssignments.AddAsync(assignment);
            await context.AthleteVariants.AddAsync(variant);
            await context.AthleteRecords.AddAsync(substituteRecord);
            await context.SaveChangesAsync();
        }

        var structureClientMock = new Mock<IStructureClient>();
        structureClientMock
            .Setup(c => c.GetAthleteCategoriesAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AthleteCategoryDto(categoryId, Guid.NewGuid(), "Plantel Superior", null)]);
        structureClientMock
            .Setup(c => c.GetAthleteByIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteDto(athleteId, "Lucas Fernández"));

        await using (var context = CreateInMemoryDbContext(pfId, dbName))
        {
            var handler = new GetTodayRoutineQueryHandler(
                new AthleteRecordRepository(context),
                new TrainingSessionRepository(context),
                new LoadCalculatorEngine(),
                structureClientMock.Object,
                new ExerciseVideoRepository(context),
                new PerformedSetRepository(context),
                adjustmentRepository: new AthleteSessionAdjustmentRepository(context),
                exerciseRepository: new ExerciseRepository(context),
                athleteVariantRepository: new AthleteVariantRepository(context));

            var result = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athleteId, Date = week2Date });

            Assert.True(result.RoutineAdapted);
            Assert.False(result.AdjustedByCoach);
            Assert.Equal(2, result.Blocks.Count);

            Assert.Equal("Entrada en calor", result.Blocks[0].Title);
            var b1Ex = Assert.Single(result.Blocks[0].Exercises);
            Assert.Equal(warmup.Id, b1Ex.ExerciseId);
            Assert.False(b1Ex.IsAdapted);

            Assert.Equal("Fuerza Principal", result.Blocks[1].Title);
            var b2Ex = Assert.Single(result.Blocks[1].Exercises);
            Assert.Equal(legPress.Id, b2Ex.ExerciseId);
            Assert.Equal("Prensa de piernas", b2Ex.Name);
            Assert.True(b2Ex.IsAdapted);
            Assert.Equal("Sentadilla", b2Ex.OriginalExerciseName);
            Assert.Equal("Variante aprobada por molestia de rodilla", b2Ex.AdaptationReason);
            Assert.Equal(135.0, b2Ex.Current1RmKg);
            Assert.Equal(107.5, b2Ex.WeightKg);
        }
    }
}