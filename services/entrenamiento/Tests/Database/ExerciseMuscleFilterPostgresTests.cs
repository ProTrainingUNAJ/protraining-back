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
    private Exercise _benchPress = null!;
    private Exercise _pushUp = null!;
    private Exercise _plank = null!;
    private Exercise _crunch = null!;
    private Exercise _militaryPress = null!;
    private Exercise _lateralRaise = null!;

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

        // KAN-633: Ejercicios de pecho, core y hombros (en inglés y español en base)
        _benchPress = Exercise.Create(
            name: "Press de Banca",
            muscleGroup: "Pecho",
            primaryMuscles: ["chest"]
        );

        _pushUp = Exercise.Create(
            name: "Flexiones de Brazo",
            muscleGroup: "Pecho",
            primaryMuscles: ["pecho"]
        );

        _plank = Exercise.Create(
            name: "Plancha Frontal",
            muscleGroup: "Core",
            primaryMuscles: ["abdominals"]
        );

        _crunch = Exercise.Create(
            name: "Crunch Abdominal",
            muscleGroup: "Core",
            primaryMuscles: ["abdominales"]
        );

        _militaryPress = Exercise.Create(
            name: "Press Militar",
            muscleGroup: "Hombros",
            primaryMuscles: ["shoulders"]
        );

        _lateralRaise = Exercise.Create(
            name: "Vuelos Laterales",
            muscleGroup: "Hombros",
            primaryMuscles: ["hombros"]
        );

        await context.Exercises.AddRangeAsync(
            _squat, _legExtension, _deadlift,
            _benchPress, _pushUp,
            _plank, _crunch,
            _militaryPress, _lateralRaise);
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

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_RealPostgreSql_When_FilteringByPecho_Then_ReturnsMatchesBothEnglishAndSpanish()
    {
        await using var context = new AppDbContext(_options, _currentUser.Object);

        // KAN-633: Pedimos por ?muscle=pecho
        var result = await CreateHandler(context).Handle(new ListExercisesQuery
        {
            Muscle = "pecho"
        });

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, e => e.Id == _benchPress.Id);
        Assert.Contains(result.Items, e => e.Id == _pushUp.Id);
        Assert.DoesNotContain(result.Items, e => e.Id == _squat.Id);
    }

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_RealPostgreSql_When_FilteringByAbdominales_Then_ReturnsMatchesBothEnglishAndSpanish()
    {
        await using var context = new AppDbContext(_options, _currentUser.Object);

        // KAN-633: Pedimos por ?muscle=abdominales
        var result = await CreateHandler(context).Handle(new ListExercisesQuery
        {
            Muscle = "abdominales"
        });

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, e => e.Id == _plank.Id);
        Assert.Contains(result.Items, e => e.Id == _crunch.Id);
        Assert.DoesNotContain(result.Items, e => e.Id == _squat.Id);
    }

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_RealPostgreSql_When_FilteringByHombros_Then_ReturnsMatchesBothEnglishAndSpanish()
    {
        await using var context = new AppDbContext(_options, _currentUser.Object);

        // KAN-633: Pedimos por ?muscle=hombros
        var result = await CreateHandler(context).Handle(new ListExercisesQuery
        {
            Muscle = "hombros"
        });

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, e => e.Id == _militaryPress.Id);
        Assert.Contains(result.Items, e => e.Id == _lateralRaise.Id);
        Assert.DoesNotContain(result.Items, e => e.Id == _squat.Id);
    }
}

