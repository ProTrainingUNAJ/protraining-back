using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Testcontainers.PostgreSql;

namespace Entrenamiento.Tests.Database;

// E51: busqueda del catalogo compartido contra PostgreSQL real (pg_trgm, ILIKE y tildes).
public sealed class ExerciseCatalogSearchPostgresTests : IAsyncLifetime
{
    private const string PostgresImage = "postgres:16-alpine";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage(PostgresImage).Build();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _pfId = Guid.NewGuid();

    private DbContextOptions<AppDbContext> _options = null!;
    private Exercise _squat = null!;
    private Exercise _legExtension = null!;
    private Exercise _deadlift = null!;
    private Exercise _privateSquat = null!;

    private sealed class TestCurrentUser(Guid? pfId) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _currentUser.Setup(user => user.PfId).Returns((Guid?)null);

        await using (var catalogContext = new AppDbContext(_options, _currentUser.Object))
        {
            await catalogContext.Database.MigrateAsync();

            _squat = Exercise.Create(
                name: "Sentadilla trasera con barra",
                muscleGroup: "Piernas",
                equipment: "barra con pesas",
                primaryMuscles: ["cuádriceps", "glúteos"],
                secondaryMuscles: ["isquiotibiales"]);

            _legExtension = Exercise.Create(
                name: "Extension de cuadriceps en maquina",
                muscleGroup: "Piernas",
                equipment: "máquina",
                primaryMuscles: ["cuádriceps"]);

            _deadlift = Exercise.Create(
                name: "Peso muerto rumano",
                muscleGroup: "Piernas",
                equipment: "barra con pesas",
                primaryMuscles: ["isquiotibiales"]);

            await catalogContext.Exercises.AddRangeAsync(_squat, _legExtension, _deadlift);
            await catalogContext.SaveChangesAsync();
        }

        // Ejercicio privado del PF: nunca debe salir por el canal MCP.
        await using (var pfContext = new AppDbContext(_options, new TestCurrentUser(_pfId)))
        {
            _privateSquat = Exercise.Create(
                name: "Sentadilla privada del PF",
                muscleGroup: "Piernas",
                equipment: "barra con pesas",
                primaryMuscles: ["cuádriceps"]);

            await pfContext.Exercises.AddAsync(_privateSquat);
            await pfContext.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_RealPostgres_When_SearchingSentadilla_Then_ReturnsCatalogMatchesAndExcludesPrivate()
    {
        await using var context = new AppDbContext(_options, _currentUser.Object);
        var repository = new ExerciseRepository(context);

        var (items, total) = await repository.SearchCatalogAsync("sentadilla", null, null, 1, 20);

        Assert.Equal(1, total);
        var item = Assert.Single(items);
        Assert.Equal(_squat.Id, item.Id);
        Assert.DoesNotContain(items, exercise => exercise.Id == _privateSquat.Id);
    }

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_PfContext_When_SearchingSentadilla_Then_PrivateExerciseIsStillExcluded()
    {
        // Con el PF autenticado, el filtro global dejaria ver su ejercicio privado;
        // la consulta de catalogo debe seguir devolviendo solo el compartido.
        await using var context = new AppDbContext(_options, new TestCurrentUser(_pfId));
        var repository = new ExerciseRepository(context);

        var (items, total) = await repository.SearchCatalogAsync("sentadilla", null, null, 1, 20);

        Assert.Equal(1, total);
        Assert.Equal(_squat.Id, Assert.Single(items).Id);
    }

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_RealPostgres_When_SearchingByMuscleWithoutAccent_Then_ReturnsCuadricepsExercises()
    {
        await using var context = new AppDbContext(_options, _currentUser.Object);
        var repository = new ExerciseRepository(context);

        var (items, total) = await repository.SearchCatalogAsync(null, null, "cuadriceps", 1, 20);

        Assert.Equal(2, total);
        Assert.Contains(items, exercise => exercise.Id == _squat.Id);
        Assert.Contains(items, exercise => exercise.Id == _legExtension.Id);
        Assert.DoesNotContain(items, exercise => exercise.Id == _deadlift.Id);
        Assert.DoesNotContain(items, exercise => exercise.Id == _privateSquat.Id);
    }

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_RealPostgres_When_SearchingByEquipment_Then_ReturnsOnlyMatchingCatalogExercises()
    {
        await using var context = new AppDbContext(_options, _currentUser.Object);
        var repository = new ExerciseRepository(context);

        var (items, total) = await repository.SearchCatalogAsync(null, "barra con pesas", null, 1, 20);

        Assert.Equal(2, total);
        Assert.Contains(items, exercise => exercise.Id == _squat.Id);
        Assert.Contains(items, exercise => exercise.Id == _deadlift.Id);
        Assert.DoesNotContain(items, exercise => exercise.Id == _legExtension.Id);
        Assert.DoesNotContain(items, exercise => exercise.Id == _privateSquat.Id);
    }

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_RealPostgres_When_PaginatingCatalog_Then_TotalIsFullCountAndItemsRespectLimit()
    {
        await using var context = new AppDbContext(_options, _currentUser.Object);
        var repository = new ExerciseRepository(context);

        var (items, total) = await repository.SearchCatalogAsync(null, null, null, 1, 1);

        Assert.Equal(3, total);
        Assert.Single(items);
    }
}
