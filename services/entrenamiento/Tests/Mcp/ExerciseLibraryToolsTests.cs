using API.Mcp;
using Application.Interfaces.Persistence;
using Application.UseCases.Exercise.GetRestrictionsByZone;
using Application.UseCases.Exercise.GetSubstitutesByZone;
using Application.UseCases.Exercise.SearchCatalogExercises;
using Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Moq;

namespace Entrenamiento.Tests.Mcp;

// E51: pruebas de la herramienta MCP BuscarEjercicios, ObtenerSustitutos y ObtenerRestricciones.
public sealed class ExerciseLibraryToolsTests
{
    private static (
        ExerciseLibraryTools Tool,
        Mock<IExerciseRepository> Repository,
        Mock<IExerciseRestrictionRepository> RestrictionRepository) CreateTool()
    {
        var repository = new Mock<IExerciseRepository>();
        var restrictionRepository = new Mock<IExerciseRestrictionRepository>();
        var handler = new SearchCatalogExercisesQueryHandler(repository.Object);
        var substitutesHandler = new GetSubstitutesByZoneQueryHandler(restrictionRepository.Object);
        var restrictionsHandler = new GetRestrictionsByZoneQueryHandler(restrictionRepository.Object);
        var tool = new ExerciseLibraryTools(handler, substitutesHandler, restrictionsHandler, NullLogger<ExerciseLibraryTools>.Instance);
        return (tool, repository, restrictionRepository);
    }

    [Fact]
    public async Task Given_McpClient_When_CallsBuscarEjerciciosWithSentadilla_Then_ReturnsMatchingExercises()
    {
        // ARRANGE
        var (tool, repository, _) = CreateTool();
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
        var (tool, repository, _) = CreateTool();
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
        var (tool, repository, _) = CreateTool();
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
        var (tool, repository, _) = CreateTool();
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
        var (tool, repository, _) = CreateTool();
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
        var (tool, repository, _) = CreateTool();
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
        var (tool, repository, _) = CreateTool();
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
        var (tool, _, _) = CreateTool();

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
        var (tool, _, _) = CreateTool();

        await Assert.ThrowsAsync<McpException>(() =>
            tool.SearchExercises(name, muscle, equipment));
    }

    [Theory]
    [InlineData("rodilla")]
    [InlineData("knee")]
    public void Given_KneeAlias_When_ResolvingRestrictionZones_Then_UsesLowerBodyRestrictionZones(string zone)
    {
        var zones = GetSubstitutesByZoneQuery.ResolveRestrictionZones(zone);

        Assert.Equal(["cuadriceps", "isquiotibiales", "gemelos"], zones);
    }

    [Fact]
    public async Task Given_UnknownZone_When_GettingSubstitutes_Then_ThrowsMcpException()
    {
        var (tool, _, restrictionRepository) = CreateTool();

        await Assert.ThrowsAsync<McpException>(() => tool.GetSubstitutes("zona-inexistente"));

        restrictionRepository.Verify(
            repository => repository.GetRestrictionsByZonesAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
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

    // -------------------------------------------------------------------------
    // Pruebas para ObtenerRestricciones (#188 · KAN-483)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Given_McpClient_When_CallsObtenerRestriccionesForRodilla_Then_ReturnsRestrictedExercisesAndSubstitutes()
    {
        // ARRANGE
        var (tool, _, restrictionRepository) = CreateTool();

        var restrictedExercise = Exercise.Create("Sentadilla con barra", "Piernas", "barra con pesas");
        var substituteExercise = Exercise.Create("Prensa de Piernas 45", "Piernas", "máquina");

        var restriction = ExerciseRestriction.Create("cuadriceps", restrictedExercise.Id, "Carga axial sobre rodilla", minPainLevel: 1);
        restriction.AddSubstitute(substituteExercise, 1);

        // Inyectamos navegación de entidad para emular EF Core Include
        typeof(ExerciseRestriction)
            .GetProperty(nameof(ExerciseRestriction.Exercise))!
            .SetValue(restriction, restrictedExercise);

        restrictionRepository
            .Setup(r => r.GetRestrictionsByZonesAsync(
                It.Is<IEnumerable<string>>(zones => zones.Contains("cuadriceps")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([restriction]);

        // ACT
        var result = await tool.GetRestrictions("rodilla");

        // ASSERT
        Assert.NotNull(result);
        Assert.Equal("rodilla", result.Zone);
        var item = Assert.Single(result.Restrictions);
        Assert.Equal(restrictedExercise.Id, item.ExerciseId);
        Assert.Equal("Sentadilla con barra", item.ExerciseName);
        Assert.Equal("cuadriceps", item.Zone);
        Assert.Equal(1, item.MinPainLevel);
        Assert.Equal("Carga axial sobre rodilla", item.Reason);
        var sub = Assert.Single(item.Substitutes);
        Assert.Equal(substituteExercise.Id, sub.ExerciseId);
        Assert.Equal("Prensa de Piernas 45", sub.Name);
    }

    [Theory]
    [InlineData("rodilla")]
    [InlineData("knee")]
    public async Task Given_KneeAlias_When_CallsObtenerRestricciones_Then_ResolvesLowerBodyZones(string zoneAlias)
    {
        // ARRANGE
        var (tool, _, restrictionRepository) = CreateTool();
        restrictionRepository
            .Setup(r => r.GetRestrictionsByZonesAsync(
                It.Is<IEnumerable<string>>(z => z.SequenceEqual(new[] { "cuadriceps", "isquiotibiales", "gemelos" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // ACT
        var result = await tool.GetRestrictions(zoneAlias);

        // ASSERT
        Assert.Equal(zoneAlias, result.Zone);
        Assert.Empty(result.Restrictions);
        restrictionRepository.Verify(
            r => r.GetRestrictionsByZonesAsync(
                It.Is<IEnumerable<string>>(z => z.SequenceEqual(new[] { "cuadriceps", "isquiotibiales", "gemelos" })),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Given_UnknownZone_When_GettingRestrictions_Then_ThrowsMcpException()
    {
        var (tool, _, restrictionRepository) = CreateTool();

        await Assert.ThrowsAsync<McpException>(() => tool.GetRestrictions("zona_invalida_total"));

        restrictionRepository.Verify(
            r => r.GetRestrictionsByZonesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Given_EmptyOrWhitespaceZone_When_GettingRestrictions_Then_ThrowsMcpException(string invalidZone)
    {
        var (tool, _, _) = CreateTool();

        await Assert.ThrowsAsync<McpException>(() => tool.GetRestrictions(invalidZone));
    }
}
