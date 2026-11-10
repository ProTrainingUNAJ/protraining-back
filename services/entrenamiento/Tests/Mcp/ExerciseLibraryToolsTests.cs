using API.Mcp;
using Application.Interfaces.Persistence;
using Application.UseCases.Exercise.SearchCatalogExercises;
using Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Moq;

namespace Entrenamiento.Tests.Mcp;

// E51: pruebas de la herramienta MCP BuscarEjercicios.
public sealed class ExerciseLibraryToolsTests
{
    private static (ExerciseLibraryTools Tool, Mock<IExerciseRepository> Repository) CreateTool()
    {
        var repository = new Mock<IExerciseRepository>();
        var handler = new SearchCatalogExercisesQueryHandler(repository.Object);
        var tool = new ExerciseLibraryTools(handler, NullLogger<ExerciseLibraryTools>.Instance);
        return (tool, repository);
    }

    [Fact]
    public async Task Given_McpClient_When_CallsBuscarEjerciciosWithSentadilla_Then_ReturnsMatchingExercises()
    {
        // ARRANGE
        var (tool, repository) = CreateTool();
        var matches = new List<Exercise>
        {
            Exercise.Create("Sentadilla con barra", "Piernas", equipment: "barra con pesas", primaryMuscles: ["cuádriceps"]),
            Exercise.Create("Sentadilla Hack con barra", "Piernas", equipment: "barra con pesas", primaryMuscles: ["cuádriceps"])
        };

        repository
            .Setup(r => r.SearchCatalogAsync("sentadilla", null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((matches, matches.Count));

        // ACT
        var result = await tool.SearchExercises(name: "sentadilla");

        // ASSERT
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Contains("Sentadilla", item.Name));
        repository.Verify(
            r => r.SearchCatalogAsync("sentadilla", null, null, 1, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Given_CatalogExercise_When_Searching_Then_ReturnsCompactFields()
    {
        // ARRANGE
        var (tool, repository) = CreateTool();
        var exercise = Exercise.Create(
            name: "Sentadilla con barra",
            muscleGroup: "Piernas",
            equipment: "barra con pesas",
            primaryMuscles: ["cuádriceps", "glúteos"],
            secondaryMuscles: ["isquiotibiales"]);

        repository
            .Setup(r => r.SearchCatalogAsync("sentadilla", null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([exercise], 1));

        // ACT
        var result = await tool.SearchExercises(name: "sentadilla");

        // ASSERT
        var item = Assert.Single(result.Items);
        Assert.Equal(exercise.Id, item.Id);
        Assert.Equal("Sentadilla con barra", item.Name);
        Assert.Equal("Piernas", item.MuscleGroup);
        Assert.Equal("barra con pesas", item.Equipment);
        Assert.Equal(new[] { "cuádriceps", "glúteos" }, item.PrimaryMuscles);
        Assert.Equal(new[] { "isquiotibiales" }, item.SecondaryMuscles);
        Assert.Equal(ExerciseType.Weight, item.ExerciseType);
    }

    [Fact]
    public async Task Given_MuscleFilter_When_Searching_Then_ForwardsMuscleToRepository()
    {
        // ARRANGE
        var (tool, repository) = CreateTool();
        repository
            .Setup(r => r.SearchCatalogAsync(null, null, "cuádriceps", 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Exercise>(), 0));

        // ACT
        await tool.SearchExercises(muscle: "cuádriceps");

        // ASSERT
        repository.Verify(
            r => r.SearchCatalogAsync(null, null, "cuádriceps", 1, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Given_EquipmentFilter_When_Searching_Then_ForwardsEquipmentToRepository()
    {
        // ARRANGE
        var (tool, repository) = CreateTool();
        repository
            .Setup(r => r.SearchCatalogAsync(null, "barra con pesas", null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Exercise>(), 0));

        // ACT
        await tool.SearchExercises(equipment: "barra con pesas");

        // ASSERT
        repository.Verify(
            r => r.SearchCatalogAsync(null, "barra con pesas", null, 1, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-5, 20)]
    [InlineData(7, 7)]
    [InlineData(500, 100)]
    public async Task Given_Limit_When_Searching_Then_ClampsBetweenOneAndMaxLimit(int requested, int expected)
    {
        // ARRANGE
        var (tool, repository) = CreateTool();
        repository
            .Setup(r => r.SearchCatalogAsync("sentadilla", null, null, 1, expected, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Exercise>(), 0));

        // ACT
        await tool.SearchExercises(name: "sentadilla", limit: requested);

        // ASSERT
        repository.Verify(
            r => r.SearchCatalogAsync("sentadilla", null, null, 1, expected, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Given_CancellationToken_When_Searching_Then_ForwardsItToRepository()
    {
        // ARRANGE
        var (tool, repository) = CreateTool();
        using var cts = new CancellationTokenSource();
        repository
            .Setup(r => r.SearchCatalogAsync("sentadilla", null, null, 1, 20, cts.Token))
            .ReturnsAsync((new List<Exercise>(), 0));

        // ACT
        await tool.SearchExercises(name: "sentadilla", cancellationToken: cts.Token);

        // ASSERT
        repository.Verify(
            r => r.SearchCatalogAsync("sentadilla", null, null, 1, 20, cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task Given_NoMatches_When_Searching_Then_ReturnsEmptyResult()
    {
        // ARRANGE
        var (tool, repository) = CreateTool();
        repository
            .Setup(r => r.SearchCatalogAsync("inexistente", null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Exercise>(), 0));

        // ACT
        var result = await tool.SearchExercises(name: "inexistente");

        // ASSERT
        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Given_NoFilters_When_Searching_Then_ThrowsMcpException()
    {
        // ARRANGE
        var (tool, _) = CreateTool();

        // ACT + ASSERT
        await Assert.ThrowsAsync<McpException>(() => tool.SearchExercises());
    }

    [Theory]
    [InlineData(" ", null, null)]
    [InlineData(null, "\t", null)]
    [InlineData(null, null, "   ")]
    public async Task Given_WhitespaceOnlyFilters_When_Searching_Then_ThrowsMcpException(
        string? name,
        string? muscle,
        string? equipment)
    {
        var (tool, _) = CreateTool();

        await Assert.ThrowsAsync<McpException>(() =>
            tool.SearchExercises(name, muscle, equipment));
    }

    [Fact]
    public async Task Given_NoFilters_When_HandlerIsCalledDirectly_Then_ThrowsArgumentException()
    {
        // ARRANGE
        var repository = new Mock<IExerciseRepository>();
        var handler = new SearchCatalogExercisesQueryHandler(repository.Object);

        // ACT + ASSERT
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new SearchCatalogExercisesQuery()));
    }
}
