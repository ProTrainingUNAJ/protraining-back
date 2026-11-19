using System.ComponentModel.DataAnnotations;
using Application.DTOs.TrainingPlans;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.TrainingPlans;
using Application.UseCases.TrainingPlans.CreatePlan;
using Application.UseCases.TrainingPlans.GetPlan;
using Application.UseCases.TrainingPlans.UpdatePlan;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Tests;

public class TrainingBlocksAndWeeklyProgressionTests
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

    // =========================================================================
    // 1. Entidad Bloque (TrainingBlock) y Jerarquía de Dominio
    // =========================================================================

    [Fact]
    public void Given_ValidData_When_CreateTrainingBlock_Then_InstantiatesProperly()
    {
        var sessionId = Guid.NewGuid();
        var block = TrainingBlock.Create(sessionId, "Entrada en calor", 1);

        Assert.NotNull(block);
        Assert.Equal(sessionId, block.TrainingSessionId);
        Assert.Equal("Entrada en calor", block.Title);
        Assert.Equal(1, block.Order);
        Assert.Empty(block.PrescribedExercises);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Given_EmptyTitle_When_CreateTrainingBlock_Then_ThrowsDomainException(string? invalidTitle)
    {
        var sessionId = Guid.NewGuid();
        var ex = Assert.Throws<DomainException>(() => TrainingBlock.Create(sessionId, invalidTitle!, 1));
        Assert.Equal("El título del bloque no puede estar vacío.", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Given_InvalidOrder_When_CreateTrainingBlock_Then_ThrowsDomainException(int invalidOrder)
    {
        var sessionId = Guid.NewGuid();
        var ex = Assert.Throws<DomainException>(() => TrainingBlock.Create(sessionId, "Bloque Principal", invalidOrder));
        Assert.Equal("El orden debe ser mayor a cero.", ex.Message);
    }

    [Fact]
    public void Given_BlockWithDuplicateExerciseOrder_When_AddPrescribedExercise_Then_ThrowsDomainException()
    {
        var block = TrainingBlock.Create(Guid.NewGuid(), "Fuerza A", 1);
        var ex1 = PrescribedExercise.Create(1, 4, "6-8", 80m, block.Id, Guid.NewGuid());
        var ex2 = PrescribedExercise.Create(1, 3, "10", 70m, block.Id, Guid.NewGuid());

        block.AddPrescribedExercise(ex1);
        var exception = Assert.Throws<DomainException>(() => block.AddPrescribedExercise(ex2));
        Assert.Contains("ya contiene un ejercicio con orden 1", exception.Message);
    }

    // =========================================================================
    // 2. Soporte de rangos de repeticiones y validación de intensidad
    // =========================================================================

    [Theory]
    [InlineData("6-8")]
    [InlineData("8-10")]
    [InlineData("10")]
    [InlineData("12-15")]
    public void Given_ValidReps_When_CreatePrescribedExercise_Then_Succeeds(string validReps)
    {
        var blockId = Guid.NewGuid();
        var exercise = PrescribedExercise.Create(1, 4, validReps, 80m, blockId, Guid.NewGuid());

        Assert.Equal(validReps, exercise.Reps);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("-4")]
    [InlineData("10-6")]   // Mínimo supera al máximo
    [InlineData("abc")]
    [InlineData("6-8-10")]
    public void Given_InvalidReps_When_CreatePrescribedExercise_Then_ThrowsDomainException(string invalidReps)
    {
        var blockId = Guid.NewGuid();
        Assert.Throws<DomainException>(() => PrescribedExercise.Create(1, 4, invalidReps, 80m, blockId, Guid.NewGuid()));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(8.5)]
    [InlineData(10.0)]
    public void Given_ValidRpe_When_CreatePrescribedExercise_Then_Succeeds(decimal rpe)
    {
        var blockId = Guid.NewGuid();
        var exercise = PrescribedExercise.Create(
            1, 4, "6-8", 80m, blockId, Guid.NewGuid(),
            intensityType: IntensityType.RPE,
            targetRpe: rpe);

        Assert.Equal(IntensityType.RPE, exercise.IntensityType);
        Assert.Equal(rpe, exercise.TargetRpe);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(10.5)]
    [InlineData(-1.0)]
    public void Given_OutOfRangeRpe_When_CreatePrescribedExercise_Then_ThrowsDomainException(decimal invalidRpe)
    {
        var blockId = Guid.NewGuid();
        var ex = Assert.Throws<DomainException>(() =>
            PrescribedExercise.Create(1, 4, "6-8", 80m, blockId, Guid.NewGuid(), intensityType: IntensityType.RPE, targetRpe: invalidRpe));

        Assert.Equal("El RPE debe estar entre 1 y 10.", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public void Given_ValidRir_When_CreatePrescribedExercise_Then_Succeeds(int rir)
    {
        var blockId = Guid.NewGuid();
        var exercise = PrescribedExercise.Create(
            1, 4, "8-10", 75m, blockId, Guid.NewGuid(),
            intensityType: IntensityType.RIR,
            targetRir: rir);

        Assert.Equal(IntensityType.RIR, exercise.IntensityType);
        Assert.Equal(rir, exercise.TargetRir);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void Given_OutOfRangeRir_When_CreatePrescribedExercise_Then_ThrowsDomainException(int invalidRir)
    {
        var blockId = Guid.NewGuid();
        var ex = Assert.Throws<DomainException>(() =>
            PrescribedExercise.Create(1, 4, "8-10", 75m, blockId, Guid.NewGuid(), intensityType: IntensityType.RIR, targetRir: invalidRir));

        Assert.Equal("El RIR debe estar entre 0 y 5.", ex.Message);
    }

    // =========================================================================
    // 3. Validación estricta en el DTO (IValidatableObject)
    // =========================================================================

    [Fact]
    public void Given_InvalidIntensityTypeInDto_When_Validated_Then_ReturnsValidationError()
    {
        var request = new PrescribedExerciseRequest(
            exerciseId: Guid.NewGuid(),
            order: 1,
            sets: 3,
            reps: "10",
            intensityType: "INVALID");

        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, context, results, validateAllProperties: true);

        Assert.Contains(results, r => r.ErrorMessage == "La métrica de intensidad debe ser 'RPE' o 'RIR'.");
    }

    [Fact]
    public void Given_InvalidRpeInDto_When_Validated_Then_ReturnsValidationError()
    {
        var request = new PrescribedExerciseRequest(
            exerciseId: Guid.NewGuid(),
            order: 1,
            sets: 3,
            reps: "6-8",
            intensityType: "RPE",
            targetRpe: 11.0m);

        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, context, results, validateAllProperties: true);

        Assert.Contains(results, r => r.ErrorMessage == "El RPE debe estar entre 1 y 10.");
    }

    [Fact]
    public void Given_InvalidRirInDto_When_Validated_Then_ReturnsValidationError()
    {
        var request = new PrescribedExerciseRequest(
            exerciseId: Guid.NewGuid(),
            order: 1,
            sets: 3,
            reps: "8-10",
            intensityType: "RIR",
            targetRir: 7);

        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, context, results, validateAllProperties: true);

        Assert.Contains(results, r => r.ErrorMessage == "El RIR debe estar entre 0 y 5.");
    }

    // =========================================================================
    // 4. Casos de Uso: Crear y Actualizar Plan con Bloques
    // =========================================================================

    [Fact]
    public async Task Given_PlanWithBlocks_When_CreatePlan_Then_ReturnsStructuredBlocksAndExercises()
    {
        var planRepoMock = new Mock<ITrainingPlanRepository>();
        var unitOfWorkMock = new Mock<IUnitOfWork>();

        planRepoMock.Setup(r => r.AllExercisesExistAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        planRepoMock.Setup(r => r.GetExerciseNamesAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());
        unitOfWorkMock.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, CancellationToken>((action, _) => action());

        var ex1Id = Guid.NewGuid();
        var ex2Id = Guid.NewGuid();

        var request = new SaveTrainingPlanRequest(
            "Plan de Potencia",
            "Preparación competitiva",
            new List<TrainingWeekRequest>
            {
                new(
                    1,
                    new List<TrainingSessionRequest>
                    {
                        new(
                            "Día A",
                            1,
                            DayOfWeek.Monday,
                            "Foco en velocidad",
                            null,
                            null,
                            null,
                            new List<TrainingBlockRequest>
                            {
                                new(
                                    "Entrada en calor",
                                    1,
                                    new List<PrescribedExerciseRequest>
                                    {
                                        new(ex1Id, 1, 3, "10", 50m, notes: "Movilidad articular")
                                    }
                                ),
                                new(
                                    "Bloque principal",
                                    2,
                                    new List<PrescribedExerciseRequest>
                                    {
                                        new(ex2Id, 1, 4, "6-8", 85m, intensityType: "RPE", targetRpe: 8.5m, notes: "Explosivo")
                                    }
                                )
                            }
                        )
                    }
                )
            }
        );

        var handler = new CreateTrainingPlanCommandHandler(planRepoMock.Object, unitOfWorkMock.Object);
        var result = await handler.Handle(new CreateTrainingPlanCommand(request));

        Assert.NotNull(result);
        Assert.Single(result.Weeks);
        var session = result.Weeks[0].Sessions[0];
        Assert.Equal(2, session.Blocks.Count);

        var block1 = session.Blocks[0];
        Assert.Equal("Entrada en calor", block1.Title);
        Assert.Equal(1, block1.Order);
        Assert.Single(block1.Exercises);
        Assert.Equal("10", block1.Exercises[0].Reps);
        Assert.Equal("Movilidad articular", block1.Exercises[0].Notes);

        var block2 = session.Blocks[1];
        Assert.Equal("Bloque principal", block2.Title);
        Assert.Equal(2, block2.Order);
        Assert.Single(block2.Exercises);
        Assert.Equal("6-8", block2.Exercises[0].Reps);
        Assert.Equal("RPE", block2.Exercises[0].IntensityType);
        Assert.Equal(8.5m, block2.Exercises[0].TargetRpe);
    }

    // =========================================================================
    // 5. Test de Regresión Anti-Rebote del PM:
    //    Leer una planificación creada antes de la migración y comprobar que
    //    devuelve sus ejercicios dentro del bloque 'Principal' con los mismos valores.
    // =========================================================================

    [Fact]
    public void Given_PlanCreatedBeforeMigrationWithoutExplicitBlocks_When_MappedToDetailDto_Then_WrapsInPrincipalBlockWithSameValues()
    {
        var plan = TrainingPlan.Create("Plan Histórico Pre-S3", "Hipertrofia");
        var week = TrainingWeek.Create(1, plan.Id);
        var session = TrainingSession.Create(week.Id, "Día Torso", 1, DayOfWeek.Tuesday, "Cuidar hombros");

        var exId1 = Guid.NewGuid();
        var exId2 = Guid.NewGuid();

        // En sprints anteriores, los ejercicios se agregaban directo a la sesión
        session.AddPrescribedExercise(PrescribedExercise.Create(1, 4, "8-10", 75.0m, session.Id, exId1));
        session.AddPrescribedExercise(PrescribedExercise.Create(2, 3, "12", 65.0m, session.Id, exId2));

        week.AddSession(session);
        plan.AddWeek(week);

        var names = new Dictionary<Guid, string>
        {
            [exId1] = "Press de Banca",
            [exId2] = "Remo con Barra"
        };

        // Act: Generar el DTO de detalle como lo hace GET /api/v1/training-plans/{id}
        var detail = TrainingPlanMapper.ToDetailDto(plan, names);

        // Assert: El anti-rebote del PM exige que devuelva sus ejercicios dentro del bloque 'Principal'
        Assert.NotNull(detail);
        Assert.Single(detail.Weeks);
        var sessionDto = detail.Weeks[0].Sessions[0];

        Assert.NotEmpty(sessionDto.Blocks);
        var principalBlock = Assert.Single(sessionDto.Blocks);
        Assert.Equal("Principal", principalBlock.Title);
        Assert.Equal(1, principalBlock.Order);

        // Los ejercicios deben estar intactos dentro del bloque 'Principal' con los mismos valores
        Assert.Equal(2, principalBlock.Exercises.Count);

        var exDto1 = principalBlock.Exercises[0];
        Assert.Equal(exId1, exDto1.ExerciseId);
        Assert.Equal("Press de Banca", exDto1.ExerciseName);
        Assert.Equal(1, exDto1.Order);
        Assert.Equal(4, exDto1.Sets);
        Assert.Equal("8-10", exDto1.Reps);
        Assert.Equal(75.0m, exDto1.TargetPercentage);

        var exDto2 = principalBlock.Exercises[1];
        Assert.Equal(exId2, exDto2.ExerciseId);
        Assert.Equal("Remo con Barra", exDto2.ExerciseName);
        Assert.Equal(2, exDto2.Order);
        Assert.Equal(3, exDto2.Sets);
        Assert.Equal("12", exDto2.Reps);
        Assert.Equal(65.0m, exDto2.TargetPercentage);
    }

    // =========================================================================
    // 6. Aislamiento Multi-Tenant (404 estricto entre PFs)
    // =========================================================================

    [Fact]
    public async Task Given_PlanCreatedByPfA_When_PfBTriesToGetPlan_Then_ReturnsNullOrThrowsNotFoundException()
    {
        var pfA = Guid.NewGuid();
        var pfB = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var plan = TrainingPlan.Create("Plan Privado PF A", "Fuerza");
        var week = TrainingWeek.Create(1, plan.Id);
        var session = TrainingSession.Create(week.Id, "Día Pierna", 1, DayOfWeek.Thursday);
        var block = TrainingBlock.Create(session.Id, "Bloque Principal", 1);
        var ex = PrescribedExercise.Create(1, 4, "6-8", 80m, block.Id, Guid.NewGuid());
        block.AddPrescribedExercise(ex);
        session.AddBlock(block);
        week.AddSession(session);
        plan.AddWeek(week);

        // Guardado con contexto del PF A
        await using (var contextA = CreateInMemoryDbContext(pfA, dbName))
        {
            await contextA.TrainingPlans.AddAsync(plan);
            await contextA.SaveChangesAsync();
        }

        // Intento de lectura con contexto del PF B
        await using (var contextB = CreateInMemoryDbContext(pfB, dbName))
        {
            var planRepo = new TrainingPlanRepository(contextB);
            var retrievedPlan = await planRepo.GetWithDetailsByIdAsync(plan.Id);

            // El filtro global de BaseEntity oculta la entidad para PF B -> retorna null
            Assert.Null(retrievedPlan);

            // El Use Case de GetPlan arroja NotFoundException (404)
            var getHandler = new GetTrainingPlanQueryHandler(planRepo);
            var exResult = await Assert.ThrowsAsync<NotFoundException>(() =>
                getHandler.Handle(new GetTrainingPlanQuery(plan.Id)));

            Assert.Contains(plan.Id.ToString(), exResult.Message);
        }
    }
}
