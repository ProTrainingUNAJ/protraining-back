using Infrastructure.Services;
using Application.Interfaces.Services;
using Application.UseCases.Exercise.ListExercises;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Testcontainers.PostgreSql;
using Xunit;

namespace Entrenamiento.Tests.Database;

// KAN-548: Test del filtro por músculo (targets.Any(t => e.PrimaryMuscles.Contains(t))) contra PostgreSQL real.
public sealed class ExerciseMuscleFilterPostgresTests : IAsyncLifetime
{
    private const string PostgresImage = "postgres:16-alpine";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage(PostgresImage).Build();
    private DbContextOptions<AppDbContext> _options = null!;
    private readonly Mock<ICurrentUser> _currentUser = new();

    private Exercise _squat = null!;
    private Exercise _deadlift = null!;
    private Exercise _legExtension = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        _currentUser.Setup(u => u.PfId).Returns((Guid?)null);

        await using var context = new AppDbContext(_options, _currentUser.Object);
        await context.Database.MigrateAsync();

        // Creamos ejercicios con cuádriceps en primary y secondary muscles
        _squat = Exercise.Create(
            name: "Sentadilla Trasera con Barra",
            muscleGroup: "Piernas",
            primaryMuscles: ["cuádriceps", "glúteos"],
            secondaryMuscles: ["isquiotibiales"]
        );

        _legExtension = Exercise.Create(
            name: "Extensión de Cuádriceps en Máquina",
            muscleGroup: "Piernas",
            primaryMuscles: ["cuádriceps"]
        );

        _deadlift = Exercise.Create(
            name: "Peso Muerto Rumano",
            muscleGroup: "Piernas",
            primaryMuscles: ["isquiotibiales"],
            secondaryMuscles: ["glúteos"]
        );

        await context.Exercises.AddRangeAsync(_squat, _legExtension, _deadlift);
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private ListExercisesQueryHandler CreateHandler(AppDbContext context) => new(new ExerciseRepository(context), new ExerciseVideoRepository(context), ExerciseMediaCatalog.Empty);

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_RealPostgreSql_When_FilteringByCuadricepsWithAccent_Then_PostgresTranslatesAndReturnsMatches()
    {
        await using var context = new AppDbContext(_options, _currentUser.Object);

        // Pedimos por ?muscle=cuádriceps (con tilde)
        var result = await CreateHandler(context).Handle(new ListExercisesQuery
        {
            Muscle = "cuádriceps"
        });

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, e => e.Id == _squat.Id);
        Assert.Contains(result.Items, e => e.Id == _legExtension.Id);
        Assert.DoesNotContain(result.Items, e => e.Id == _deadlift.Id);
    }

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_RealPostgreSql_When_FilteringByCuadricepsWithoutAccent_Then_PostgresTranslatesAndReturnsMatches()
    {
        await using var context = new AppDbContext(_options, _currentUser.Object);

        // Pedimos por ?muscle=cuadriceps (sin tilde)
        var result = await CreateHandler(context).Handle(new ListExercisesQuery
        {
            Muscle = "cuadriceps"
        });

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, e => e.Id == _squat.Id);
        Assert.Contains(result.Items, e => e.Id == _legExtension.Id);
    }

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_RealPostgreSql_When_FilteringByCuadricepsGrouped_Then_PostgresTranslatesAndReturnsBases()
    {
        await using var context = new AppDbContext(_options, _currentUser.Object);

        // Pedimos agrupado (GroupVariants = true) que usa ListBasesAsync
        var result = await CreateHandler(context).Handle(new ListExercisesQuery
        {
            Muscle = "cuádriceps",
            GroupVariants = true
        });

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, e => e.Id == _squat.Id);
        Assert.Contains(result.Items, e => e.Id == _legExtension.Id);
    }
}

