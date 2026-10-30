using Application.DTOs.Exercise;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Exercise.ListExercises;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Tests;

public class ListExercisesTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _contextOptions;
    private readonly Guid _pfId = Guid.NewGuid();

    private sealed class TestCurrentUser(Guid? pfId) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    public ListExercisesTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _contextOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private AppDbContext CreateContext(Guid? customPfId = null) => new(_contextOptions, new TestCurrentUser(customPfId ?? _pfId));
    private AppDbContext CreateCatalogContext() => new(_contextOptions, new TestCurrentUser(null));


    [Fact]
    public async Task Listar_ConPaginacion_CalculaTotalYPaginas()
    {
        // ARRANGE
        var repoMock = new Mock<IExerciseRepository>();
        var dummyExercises = Enumerable.Range(1, 20)
            .Select(i => Exercise.Create($"Ejercicio {i}", "Pecho"))
            .ToList();

        repoMock.Setup(r => r.ListAsync(null, null, null, false, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((dummyExercises, 876));

        var handler = new ListExercisesQueryHandler(repoMock.Object);

        // ACT
        var query = new ListExercisesQuery { Page = 1, PageSize = 20 };
        var result = await handler.Handle(query);

        // ASSERT
        Assert.NotNull(result);
        Assert.Equal(20, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(876, result.TotalCount);
        Assert.Equal(44, result.TotalPages);
    }

    [Fact]
    public async Task Listar_ConBusquedaPorTexto_FiltraPorNombre()
    {
        // ARRANGE
        var repoMock = new Mock<IExerciseRepository>();
        var sentadillas = new List<Exercise>
        {
            Exercise.Create("Sentadilla Trasera", "Piernas"),
            Exercise.Create("Sentadilla Frontal", "Piernas"),
            Exercise.Create("Sentadilla Búlgara", "Piernas"),
            Exercise.Create("Sentadilla Sumo", "Piernas"),
            Exercise.Create("Sentadilla Goblet", "Piernas")
        };

        repoMock.Setup(r => r.ListAsync("sentadilla", null, null, false, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((sentadillas, 5));

        var handler = new ListExercisesQueryHandler(repoMock.Object);

        // ACT
        var query = new ListExercisesQuery { Search = "sentadilla", Page = 1, PageSize = 20 };
        var result = await handler.Handle(query);

        // ASSERT
        Assert.NotNull(result);
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        Assert.All(result.Items, item => Assert.Contains("Sentadilla", item.Name));
    }

    [Fact]
    public async Task Listar_UltimaPagina_RetornaItemsRestantes()
    {
        // ARRANGE
        var repoMock = new Mock<IExerciseRepository>();
        var lastPageExercises = Enumerable.Range(861, 16)
            .Select(i => Exercise.Create($"Ejercicio {i}", "Espalda"))
            .ToList();

        repoMock.Setup(r => r.ListAsync(null, null, null, false, 44, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((lastPageExercises, 876));

        var handler = new ListExercisesQueryHandler(repoMock.Object);

        // ACT
        var query = new ListExercisesQuery { Page = 44, PageSize = 20 };
        var result = await handler.Handle(query);

        // ASSERT
        Assert.NotNull(result);
        Assert.Equal(16, result.Items.Count);
        Assert.Equal(44, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(876, result.TotalCount);
        Assert.Equal(44, result.TotalPages);
    }

    [Fact]
    public async Task Listar_ConPageSizeMayorA100_AplicaTopeMaximo()
    {
        // ARRANGE
        var repoMock = new Mock<IExerciseRepository>();
        var exercises100 = Enumerable.Range(1, 100)
            .Select(i => Exercise.Create($"Ejercicio {i}", "Piernas"))
            .ToList();

        repoMock.Setup(r => r.ListAsync(null, null, null, false, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((exercises100, 876));

        var handler = new ListExercisesQueryHandler(repoMock.Object);

        // ACT
        var query = new ListExercisesQuery { Page = 1, PageSize = 500 };
        var result = await handler.Handle(query);

        // ASSERT
        Assert.NotNull(result);
        Assert.Equal(100, result.PageSize);
        Assert.Equal(100, result.Items.Count);
        Assert.Equal(876, result.TotalCount);
        Assert.Equal(9, result.TotalPages);
        repoMock.Verify(r => r.ListAsync(null, null, null, false, 1, 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Listar_ConParametrosInvalidos_NormalizaValoresPorDefecto()
    {
        // ARRANGE
        var repoMock = new Mock<IExerciseRepository>();
        var defaultExercises = Enumerable.Range(1, 20)
            .Select(i => Exercise.Create($"Ejercicio {i}", "Brazos"))
            .ToList();

        repoMock.Setup(r => r.ListAsync(null, null, null, false, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((defaultExercises, 100));

        var handler = new ListExercisesQueryHandler(repoMock.Object);

        // ACT
        var query = new ListExercisesQuery { Page = 0, PageSize = -5 };
        var result = await handler.Handle(query);

        // ASSERT
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(20, result.Items.Count);
        Assert.Equal(100, result.TotalCount);
        Assert.Equal(5, result.TotalPages);
        repoMock.Verify(r => r.ListAsync(null, null, null, false, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Listar_SinCoincidencias_RetornaListaVacia()
    {
        // ARRANGE
        var repoMock = new Mock<IExerciseRepository>();
        repoMock.Setup(r => r.ListAsync("inexistente", null, null, false, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Exercise>(), 0));

        var handler = new ListExercisesQueryHandler(repoMock.Object);

        // ACT
        var query = new ListExercisesQuery { Search = "inexistente" };
        var result = await handler.Handle(query);

        // ASSERT
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task Listar_ConUsedByPfTrue_PasaFiltroAlRepositorio()
    {
        // ARRANGE
        var repoMock = new Mock<IExerciseRepository>();
        var pfExercises = new List<Exercise>
        {
            Exercise.Create("Sentadilla", "Piernas"),
            Exercise.Create("Press de banca", "Pecho")
        };

        repoMock.Setup(r => r.ListAsync(null, null, null, true, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((pfExercises, 2));

        var handler = new ListExercisesQueryHandler(repoMock.Object);

        // ACT
        var query = new ListExercisesQuery { UsedByPf = true, Page = 1, PageSize = 20 };
        var result = await handler.Handle(query);

        // ASSERT
        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
        repoMock.Verify(r => r.ListAsync(null, null, null, true, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Listar_ConErrorDeTipeo_RetornaPorSimilitud()
    {
        // ARRANGE
        var repoMock = new Mock<IExerciseRepository>();
        var sentadillas = new List<Exercise>
        {
            Exercise.Create("Sentadilla completa con barra", "Piernas"),
            Exercise.Create("Sentadilla frontal", "Piernas")
        };

        repoMock.Setup(r => r.ListAsync("setnadilla", null, null, false, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((sentadillas, 2));

        var handler = new ListExercisesQueryHandler(repoMock.Object);

        // ACT
        var query = new ListExercisesQuery { Search = "setnadilla", Page = 1, PageSize = 20 };
        var result = await handler.Handle(query);

        // ASSERT
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, e => e.Name.Contains("Sentadilla"));
    }

    [Fact]
    public async Task Repository_ListAsync_SinFiltros_RetornaPaginaYTotal()
    {
        // ARRANGE: Sembrar 25 ejercicios en SQLite
        await using (var seedContext = CreateContext())
        {
            for (int i = 1; i <= 25; i++)
            {
                await seedContext.Exercises.AddAsync(Exercise.Create($"Ejercicio {i:D2}", "Pecho"));
            }
            await seedContext.SaveChangesAsync();
        }

        // ACT: Pedir página 1 con pageSize = 10
        await using (var actContext = CreateContext())
        {
            var repo = new ExerciseRepository(actContext);
            var (items, totalCount) = await repo.ListAsync(null, null, null, false, 1, 10);

            // ASSERT
            Assert.Equal(10, items.Count);
            Assert.Equal(25, totalCount);
        }
    }

    [Fact]
    public async Task Repository_ListAsync_ConGrupoMuscular_FiltraPorGrupo()
    {
        // ARRANGE: 15 de Piernas y 10 de Pecho (total 25)
        await using (var seedContext = CreateContext())
        {
            for (int i = 1; i <= 15; i++)
            {
                await seedContext.Exercises.AddAsync(Exercise.Create($"Pierna {i}", "Piernas"));
            }
            for (int i = 1; i <= 10; i++)
            {
                await seedContext.Exercises.AddAsync(Exercise.Create($"Pecho {i}", "Pecho"));
            }
            await seedContext.SaveChangesAsync();
        }

        // ACT: Filtrar por "Piernas"
        await using (var actContext = CreateContext())
        {
            var repo = new ExerciseRepository(actContext);
            var (items, totalCount) = await repo.ListAsync(null, null, "Piernas", false, 1, 10);

            // ASSERT
            Assert.Equal(10, items.Count);
            Assert.Equal(15, totalCount);
            Assert.All(items, e => Assert.Equal("Piernas", e.MuscleGroup));
        }
    }

    [Fact]
    public async Task Repository_ListAsync_UltimaPagina_RetornaRestantes()
    {
        // ARRANGE: 25 ejercicios, página 3 con pageSize = 10 -> debe traer 5
        await using (var seedContext = CreateContext())
        {
            for (int i = 1; i <= 25; i++)
            {
                await seedContext.Exercises.AddAsync(Exercise.Create($"Ejercicio {i:D2}", "Hombros"));
            }
            await seedContext.SaveChangesAsync();
        }

        // ACT: Página 3
        await using (var actContext = CreateContext())
        {
            var repo = new ExerciseRepository(actContext);
            var (items, totalCount) = await repo.ListAsync(null, null, null, false, 3, 10);

            // ASSERT
            Assert.Equal(5, items.Count);
            Assert.Equal(25, totalCount);
        }
    }

    [Fact]
    public async Task Repository_ListAsync_ConUsedByPf_RetornaSoloEjerciciosDelPlan()
    {
        var sentadilla = Exercise.Create("Sentadilla", "Piernas");
        var pressBanca = Exercise.Create("Press de banca", "Pecho");
        var pesoMuerto = Exercise.Create("Peso muerto rumano", "Piernas");
        var curlBiceps = Exercise.Create("Curl de bíceps", "Brazos");

        await using (var seedContext = CreateContext())
        {
            await seedContext.Exercises.AddRangeAsync(sentadilla, pressBanca, pesoMuerto, curlBiceps);

            var plan = TrainingPlan.Create("Fuerza base");
            var week = TrainingWeek.Create(1, plan.Id);
            var session = TrainingSession.Create(week.Id, "Día 1", 1, DayOfWeek.Monday);

            session.AddPrescribedExercise(PrescribedExercise.Create(1, 4, 8, 80m, session.Id, sentadilla.Id));
            session.AddPrescribedExercise(PrescribedExercise.Create(2, 4, 8, 75m, session.Id, pressBanca.Id));
            session.AddPrescribedExercise(PrescribedExercise.Create(3, 4, 10, 70m, session.Id, pesoMuerto.Id));

            week.AddSession(session);
            plan.AddWeek(week);

            await seedContext.TrainingPlans.AddAsync(plan);
            await seedContext.SaveChangesAsync();
        }

        // ACT: Consulta con usedByPf = true
        await using (var actContext = CreateContext())
        {
            var repo = new ExerciseRepository(actContext);
            var (items, totalCount) = await repo.ListAsync(null, null, null, true, 1, 20);

            // ASSERT: Solo los 3 ejercicios del plan
            Assert.Equal(3, totalCount);
            Assert.Equal(3, items.Count);
            Assert.Contains(items, e => e.Id == sentadilla.Id);
            Assert.Contains(items, e => e.Id == pressBanca.Id);
            Assert.Contains(items, e => e.Id == pesoMuerto.Id);
            Assert.DoesNotContain(items, e => e.Id == curlBiceps.Id);
        }
    }

    [Fact]
    public async Task Repository_ListAsync_ConUsedByPf_UneEjerciciosDePlanesYRecords()
    {
        var sentadilla = Exercise.Create("Sentadilla", "Piernas");
        var pressBanca = Exercise.Create("Press de banca", "Pecho");
        var dominadas = Exercise.Create("Dominadas", "Espalda");
        var fondos = Exercise.Create("Fondos", "Pecho");

        await using (var seedContext = CreateContext())
        {
            await seedContext.Exercises.AddRangeAsync(sentadilla, pressBanca, dominadas, fondos);

            var plan = TrainingPlan.Create("Fuerza 1");
            var week = TrainingWeek.Create(1, plan.Id);
            var session = TrainingSession.Create(week.Id, "Sesión A", 1, DayOfWeek.Wednesday);
            session.AddPrescribedExercise(PrescribedExercise.Create(1, 3, 5, 85m, session.Id, sentadilla.Id));
            session.AddPrescribedExercise(PrescribedExercise.Create(2, 3, 5, 80m, session.Id, pressBanca.Id));
            week.AddSession(session);
            plan.AddWeek(week);
            await seedContext.TrainingPlans.AddAsync(plan);

            var record = AthleteRecord.Create(Guid.NewGuid(), dominadas.Id, 25m, RecordSource.Tested, DateTime.UtcNow);
            await seedContext.AthleteRecords.AddAsync(record);

            await seedContext.SaveChangesAsync();
        }

        // ACT
        await using (var actContext = CreateContext())
        {
            var repo = new ExerciseRepository(actContext);
            var (items, totalCount) = await repo.ListAsync(null, null, null, true, 1, 20);

            // ASSERT: 3 ejercicios (2 del plan + 1 del récord)
            Assert.Equal(3, totalCount);
            Assert.Contains(items, e => e.Id == sentadilla.Id);
            Assert.Contains(items, e => e.Id == pressBanca.Id);
            Assert.Contains(items, e => e.Id == dominadas.Id);
            Assert.DoesNotContain(items, e => e.Id == fondos.Id);
        }
    }

    [Fact]
    public async Task Repository_ListAsync_ConUsedByPf_NoMuestraEjerciciosDeOtroPf()
    {
        var pf1Id = _pfId;
        var pf2Id = Guid.NewGuid();

        var ejercicioPf1 = Exercise.Create("Sentadilla Frontal", "Piernas");
        var ejercicioPf2 = Exercise.Create("Remo con Barra", "Espalda");

        // Sembrar ejercicios en el catálogo general
        await using (var catalogContext = CreateCatalogContext())
        {
            await catalogContext.Exercises.AddRangeAsync(ejercicioPf1, ejercicioPf2);
            await catalogContext.SaveChangesAsync();
        }

        // PF 1 crea su plan
        await using (var seedContext1 = CreateContext(pf1Id))
        {
            var plan1 = TrainingPlan.Create("Plan PF 1");
            var week1 = TrainingWeek.Create(1, plan1.Id);
            var session1 = TrainingSession.Create(week1.Id, "S1", 1, DayOfWeek.Monday);
            session1.AddPrescribedExercise(PrescribedExercise.Create(1, 3, 10, null, session1.Id, ejercicioPf1.Id));
            week1.AddSession(session1);
            plan1.AddWeek(week1);
            await seedContext1.TrainingPlans.AddAsync(plan1);
            await seedContext1.SaveChangesAsync();
        }

        // PF 2 crea su plan
        await using (var seedContext2 = CreateContext(pf2Id))
        {
            var plan2 = TrainingPlan.Create("Plan PF 2");
            var week2 = TrainingWeek.Create(1, plan2.Id);
            var session2 = TrainingSession.Create(week2.Id, "S2", 1, DayOfWeek.Tuesday);
            session2.AddPrescribedExercise(PrescribedExercise.Create(1, 3, 10, null, session2.Id, ejercicioPf2.Id));
            week2.AddSession(session2);
            plan2.AddWeek(week2);
            await seedContext2.TrainingPlans.AddAsync(plan2);
            await seedContext2.SaveChangesAsync();
        }

        // ACT: PF 2 consulta con usedByPf = true
        await using (var actContext2 = CreateContext(pf2Id))
        {
            var repo = new ExerciseRepository(actContext2);
            var (items, totalCount) = await repo.ListAsync(null, null, null, true, 1, 20);

            // ASSERT: PF 2 solo ve su propio ejercicio, NO el de PF 1
            Assert.Equal(1, totalCount);
            Assert.Single(items);
            Assert.Equal(ejercicioPf2.Id, items[0].Id);
            Assert.DoesNotContain(items, e => e.Id == ejercicioPf1.Id);
        }
    }

    [Fact]
    public async Task Repository_ListAsync_PfSinActividad_RetornaListaVacia()
    {
        var pfNuevo = Guid.NewGuid();

        await using (var seedContext = CreateContext())
        {
            await seedContext.Exercises.AddAsync(Exercise.Create("Press Militar", "Hombros"));
            await seedContext.SaveChangesAsync();
        }

        // ACT
        await using (var actContext = CreateContext(pfNuevo))
        {
            var repo = new ExerciseRepository(actContext);
            var (items, totalCount) = await repo.ListAsync(null, null, null, true, 1, 20);

            // ASSERT
            Assert.Empty(items);
            Assert.Equal(0, totalCount);
        }
    }

    [Fact]
    public async Task Repository_ListAsync_ConUsedByPfFalse_RetornaTodoElCatalogo()
    {
        var ex1 = Exercise.Create("Sentadilla", "Piernas");
        var ex2 = Exercise.Create("Press Francés", "Brazos");

        await using (var seedContext = CreateContext())
        {
            await seedContext.Exercises.AddRangeAsync(ex1, ex2);

            var plan = TrainingPlan.Create("Plan Simple");
            var week = TrainingWeek.Create(1, plan.Id);
            var session = TrainingSession.Create(week.Id, "S1", 1, DayOfWeek.Friday);
            session.AddPrescribedExercise(PrescribedExercise.Create(1, 3, 10, null, session.Id, ex1.Id));
            week.AddSession(session);
            plan.AddWeek(week);
            await seedContext.TrainingPlans.AddAsync(plan);
            await seedContext.SaveChangesAsync();
        }

        // ACT: Consulta con usedByPf = false (todo el catálogo)
        await using (var actContext = CreateContext())
        {
            var repo = new ExerciseRepository(actContext);
            var (items, totalCount) = await repo.ListAsync(null, null, null, false, 1, 20);

            // ASSERT: Retorna todos los ejercicios del catálogo
            Assert.True(totalCount >= 2);
            Assert.Contains(items, e => e.Id == ex1.Id);
            Assert.Contains(items, e => e.Id == ex2.Id);
        }
    }

    // ==========================================
    // 3. Test de Entidad (Domain)
    // ==========================================

    [Fact]
    public void Create_SinImagenesNiAnimacion_CreaEjercicioCorrectamente()
    {
        // Arrange & Act
        var exercise = Exercise.Create(
            name: "Press de Banca",
            muscleGroup: "Pecho",
            description: null,
            animationUrl: null,
            videoUrl: null,
            equipment: null,
            images: null
        );

        // Assert
        Assert.NotNull(exercise);
        Assert.Null(exercise.AnimationUrl);
        Assert.Empty(exercise.Images);
    }
}
