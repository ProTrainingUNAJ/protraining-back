namespace Asistente.Tests;

using Application.Common.Ai;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Infrastructure.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Pruebas unitarias para el armado de prompts con ejercicios disponibles del catálogo (#087 · KAN-382).
/// Valida la inyección de ejercicios, IDs oficiales, directivas JSON, filtrado por grupo muscular
/// y resiliencia ante contingencias del catálogo.
/// </summary>
public sealed class PromptEjerciciosDisponiblesTests
{
    private readonly Guid _athleteId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private readonly Guid _benchPressId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private readonly Guid _squatId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");
    private readonly Guid _pullUpId = Guid.Parse("cccccccc-1111-2222-3333-444444444444");

    [Fact]
    public void BuildPrompt_WhenAvailableExercisesProvided_IncludesCatalogSectionAndIds()
    {
        // Arrange
        var catalog = new List<CatalogExerciseDto>
        {
            new(_benchPressId, "Press de Banca Plano", "Pecho", "Barra olímpica y banco"),
            new(_squatId, "Sentadilla Trasera", "Piernas", "Rack y barra")
        };

        var request = new RoutineSuggestionRequestDto(
            AthleteId: _athleteId,
            Goal: "Hipertrofia",
            MuscleGroupEmphasis: ["Pecho", "Piernas"],
            AvailableExercises: catalog
        );

        // Act
        var prompt = RoutinePromptBuilder.BuildPrompt(request);

        // Assert
        Assert.Contains("EJERCICIOS DISPONIBLES EN LA BIBLIOTECA OFICIAL DEL CLUB:", prompt);
        Assert.Contains($"[Id: {_benchPressId}] Press de Banca Plano, Grupo: Pecho, Equipo: Barra olímpica y banco", prompt);
        Assert.Contains($"[Id: {_squatId}] Sentadilla Trasera, Grupo: Piernas, Equipo: Rack y barra", prompt);
        Assert.Contains("DIRECTIVAS DEL CATÁLOGO:", prompt);
        Assert.Contains("ejercicioId", prompt);
        Assert.Contains(_athleteId.ToString(), prompt);
        Assert.Contains("Hipertrofia", prompt);
    }

    [Fact]
    public void BuildPrompt_WhenNoAvailableExercises_BuildsGeneralPromptWithoutCatalogDirectives()
    {
        // Arrange
        var request = new RoutineSuggestionRequestDto(
            AthleteId: _athleteId,
            Goal: "Fuerza Máxima",
            MuscleGroupEmphasis: ["Espalda"],
            AvailableExercises: null
        );

        // Act
        var prompt = RoutinePromptBuilder.BuildPrompt(request);

        // Assert
        Assert.DoesNotContain("EJERCICIOS DISPONIBLES EN LA BIBLIOTECA OFICIAL DEL CLUB:", prompt);
        Assert.DoesNotContain("DIRECTIVAS DEL CATÁLOGO:", prompt);
        Assert.Contains("DIRECTIVAS:", prompt);
        Assert.Contains("Fuerza Máxima", prompt);
        Assert.Contains("Espalda", prompt);
        Assert.Contains("REGLAS ESTRICTAS DE PRIVACIDAD", prompt);
    }

    [Fact]
    public void FilterRelevantExercises_WhenEmphasisMatches_PrioritizesMatchingExercises()
    {
        // Arrange
        var catalog = new List<CatalogExerciseDto>
        {
            new(_benchPressId, "Press Banca", "Pecho", "Barra"),
            new(_pullUpId, "Dominadas", "Espalda", "Barra"),
            new(_squatId, "Sentadilla", "Piernas", "Rack")
        };

        // Act
        var filtered = RoutinePromptBuilder.FilterRelevantExercises(catalog, ["Pecho"], maxExercises: 2);

        // Assert
        Assert.Equal(2, filtered.Count);
        Assert.Equal("Press Banca", filtered[0].Name); // Matching first
        Assert.Contains(filtered, ex => ex.Name == "Dominadas" || ex.Name == "Sentadilla"); // Remaining padded
    }

    [Fact]
    public void FilterRelevantExercises_WhenEmphasisIsEmpty_ReturnsTopExercisesUpToMax()
    {
        // Arrange
        var catalog = Enumerable.Range(1, 30)
            .Select(i => new CatalogExerciseDto(Guid.NewGuid(), $"Ejercicio {i}", "General", "Mancuernas"))
            .ToList();

        // Act
        var filtered = RoutinePromptBuilder.FilterRelevantExercises(catalog, null, maxExercises: 15);

        // Assert
        Assert.Equal(15, filtered.Count);
        Assert.Equal("Ejercicio 1", filtered[0].Name);
    }

    [Fact]
    public void FilterRelevantExercises_WhenCatalogIsEmpty_ReturnsEmptyList()
    {
        // Act
        var filtered = RoutinePromptBuilder.FilterRelevantExercises([], ["Bíceps"]);

        // Assert
        Assert.Empty(filtered);
    }

    [Fact]
    public async Task ResilientAiService_WhenCatalogServiceAvailableAndRequestHasNoExercises_EnrichesRequestBeforeProviderCall()
    {
        // Arrange
        var catalog = new List<CatalogExerciseDto>
        {
            new(_benchPressId, "Press de Banca", "Pecho", "Barra"),
            new(_pullUpId, "Dominadas Pronas", "Espalda", "Barra")
        };

        var catalogServiceMock = new Mock<IExerciseCatalogService>();
        catalogServiceMock
            .Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);

        RoutineSuggestionRequestDto? receivedRequestByProvider = null;
        var providerMock = new Mock<IAiClient>();
        providerMock.Setup(p => p.ProviderName).Returns("MockProvider");
        providerMock
            .Setup(p => p.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .Callback<RoutineSuggestionRequestDto, CancellationToken>((req, _) => receivedRequestByProvider = req)
            .ReturnsAsync(new RoutineSuggestionResponseDto(
                AthleteId: _athleteId,
                ProposedTitle: "Rutina Test",
                JustificationSummary: "Test justificación",
                Exercises:
                [
                    new SuggestedExerciseDto("Press de Banca", 4, 8, 75m, _benchPressId)
                ],
                ProviderUsed: "MockProvider"
            ));

        var service = new ResilientAiService(
            providers: [providerMock.Object],
            semanticValidator: null,
            responseCache: null,
            currentUser: null,
            auditService: null,
            catalogService: catalogServiceMock.Object,
            logger: NullLogger<ResilientAiService>.Instance
        );

        var request = new RoutineSuggestionRequestDto(
            AthleteId: _athleteId,
            Goal: "Fuerza",
            MuscleGroupEmphasis: ["Pecho"]
        );

        // Act
        var result = await service.SuggestRoutineAsync(request);

        // Assert
        Assert.NotNull(result);
        catalogServiceMock.Verify(c => c.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(receivedRequestByProvider);
        Assert.NotNull(receivedRequestByProvider.AvailableExercises);
        Assert.NotEmpty(receivedRequestByProvider.AvailableExercises);
        Assert.Equal("Press de Banca", receivedRequestByProvider.AvailableExercises[0].Name);
    }

    [Fact]
    public async Task ResilientAiService_WhenCatalogServiceFails_ProceedsGracefullyWithoutThrowing()
    {
        // Arrange
        var catalogServiceMock = new Mock<IExerciseCatalogService>();
        catalogServiceMock
            .Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Entrenamiento microservice down"));

        var providerMock = new Mock<IAiClient>();
        providerMock.Setup(p => p.ProviderName).Returns("FallbackProvider");
        providerMock
            .Setup(p => p.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutineSuggestionResponseDto(
                AthleteId: _athleteId,
                ProposedTitle: "Rutina Respaldo",
                JustificationSummary: "Generada sin catálogo",
                Exercises:
                [
                    new SuggestedExerciseDto("Flexiones de brazos", 3, 12, 0m)
                ],
                ProviderUsed: "FallbackProvider"
            ));

        var service = new ResilientAiService(
            providers: [providerMock.Object],
            semanticValidator: null,
            responseCache: null,
            currentUser: null,
            auditService: null,
            catalogService: catalogServiceMock.Object,
            logger: NullLogger<ResilientAiService>.Instance
        );

        var request = new RoutineSuggestionRequestDto(
            AthleteId: _athleteId,
            Goal: "Resistencia"
        );

        // Act
        var result = await service.SuggestRoutineAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Rutina Respaldo", result.ProposedTitle);
        catalogServiceMock.Verify(c => c.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
