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

    private sealed class TestCurrentUser(Guid pfId) : ICurrentUser
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

    private AppDbContext CreateContext() => new(_contextOptions, new TestCurrentUser(_pfId));

    // ==========================================
    // 1. Criterios de Aceptación y UseCase Tests
    // ==========================================

    [Fact]
    public async Task Dado876Ejercicios_CuandoPidePagina1ConPageSize20_EntoncesRecibe20ItemsTotalCount876YTotalPages44()
    {
        // ARRANGE: Criterio de aceptación 1
        var repoMock = new Mock<IExerciseRepository>();
        var dummyExercises = Enumerable.Range(1, 20)
            .Select(i => Exercise.Create($"Ejercicio {i}", "Pecho"))
            .ToList();

        repoMock.Setup(r => r.ListAsync(null, null, null, 1, 20, It.IsAny<CancellationToken>()))
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
    public async Task DadoCatalogoCon876Ejercicios_CuandoBuscaSentadilla_EntoncesTotalCountEsElDeResultadosFiltradosYNoDelCatalogo()
    {
        // ARRANGE: Criterio de aceptación 2 (Filtro por búsqueda)
        var repoMock = new Mock<IExerciseRepository>();
        var sentadillas = new List<Exercise>
        {
            Exercise.Create("Sentadilla Trasera", "Piernas"),
            Exercise.Create("Sentadilla Frontal", "Piernas"),
            Exercise.Create("Sentadilla Búlgara", "Piernas"),
            Exercise.Create("Sentadilla Sumo", "Piernas"),
            Exercise.Create("Sentadilla Goblet", "Piernas")
        };

        repoMock.Setup(r => r.ListAsync("sentadilla", null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((sentadillas, 5));

        var handler = new ListExercisesQueryHandler(repoMock.Object);

        // ACT
        var query = new ListExercisesQuery { Search = "sentadilla", Page = 1, PageSize = 20 };
        var result = await handler.Handle(query);

        // ASSERT
        Assert.NotNull(result);
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(5, result.TotalCount); // Total filtrado, no 876
        Assert.Equal(1, result.TotalPages);
        Assert.All(result.Items, item => Assert.Contains("Sentadilla", item.Name));
    }

    [Fact]
    public async Task DadoCatalogoCon876Ejercicios_CuandoPideUltimaPagina44ConPageSize20_EntoncesRecibe16ItemsIncompletosYTotalPages44()
    {
        // ARRANGE: Última página incompleta (876 total - 43 * 20 = 16 restantes)
        var repoMock = new Mock<IExerciseRepository>();
        var lastPageExercises = Enumerable.Range(861, 16)
            .Select(i => Exercise.Create($"Ejercicio {i}", "Espalda"))
            .ToList();

        repoMock.Setup(r => r.ListAsync(null, null, null, 44, 20, It.IsAny<CancellationToken>()))
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
    public async Task DadoCatalogo_CuandoPidePageSizeMayorA100_EntoncesAplicaTopeDe100()
    {
        // ARRANGE: Solicitar pageSize = 500 (debe toparse a 100)
        var repoMock = new Mock<IExerciseRepository>();
        var exercises100 = Enumerable.Range(1, 100)
            .Select(i => Exercise.Create($"Ejercicio {i}", "Piernas"))
            .ToList();

        repoMock.Setup(r => r.ListAsync(null, null, null, 1, 100, It.IsAny<CancellationToken>()))
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
        Assert.Equal(9, result.TotalPages); // ceil(876 / 100) = 9
        repoMock.Verify(r => r.ListAsync(null, null, null, 1, 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DadoParametrosMenoresAUno_CuandoConsulta_EntoncesNormalizaAPagina1YPageSize20()
    {
        // ARRANGE: Parámetros inválidos o menores a 1 (ej. page = 0, pageSize = -5)
        var repoMock = new Mock<IExerciseRepository>();
        var defaultExercises = Enumerable.Range(1, 20)
            .Select(i => Exercise.Create($"Ejercicio {i}", "Brazos"))
            .ToList();

        repoMock.Setup(r => r.ListAsync(null, null, null, 1, 20, It.IsAny<CancellationToken>()))
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
        repoMock.Verify(r => r.ListAsync(null, null, null, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DadoCatalogoVacioOFiltroSinCoincidencias_CuandoConsulta_EntoncesDevuelveTotalCount0YTotalPages0()
    {
        // ARRANGE
        var repoMock = new Mock<IExerciseRepository>();
        repoMock.Setup(r => r.ListAsync("inexistente", null, null, 1, 20, It.IsAny<CancellationToken>()))
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

    // ==========================================
    // 2. Integration Tests de ExerciseRepository
    // ==========================================

    [Fact]
    public async Task Repository_ListAsync_DadoEjerciciosEnBase_CuandoConsultaSinFiltro_EntoncesRetornaPaginaYTotalCountExacto()
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
            var (items, totalCount) = await repo.ListAsync(null, null, null, 1, 10);

            // ASSERT
            Assert.Equal(10, items.Count);
            Assert.Equal(25, totalCount);
        }
    }

    [Fact]
    public async Task Repository_ListAsync_DadoEjerciciosDeVariosGrupos_CuandoFiltraPorMuscleGroup_EntoncesTotalCountCorrespondeSoloAlGrupoFiltrado()
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
            var (items, totalCount) = await repo.ListAsync(null, null, "Piernas", 1, 10);

            // ASSERT
            Assert.Equal(10, items.Count);
            Assert.Equal(15, totalCount); // Solo los 15 de piernas
            Assert.All(items, e => Assert.Equal("Piernas", e.MuscleGroup));
        }
    }

    [Fact]
    public async Task Repository_ListAsync_DadaUltimaPaginaIncompleta_CuandoConsulta_EntoncesRetornaSoloRestantesYTotalCountCompleto()
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
            var (items, totalCount) = await repo.ListAsync(null, null, null, 3, 10);

            // ASSERT
            Assert.Equal(5, items.Count);
            Assert.Equal(25, totalCount);
        }
    }
}
