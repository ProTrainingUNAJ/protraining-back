namespace Asistente.Tests;

using System.Text.Json;
using Application.DTOs.Ai;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Ai.SuggestRoutine;
using Domain.Entities;
using Domain.Exceptions;
using Moq;
using Xunit;

/// <summary>
/// Pruebas unitarias para la validación, persistencia de borradores de rutinas y retorno del DTO (#088 · KAN-383).
/// </summary>
public sealed class PersistirBorradorRutinaTests
{
    private readonly Guid _athleteId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private readonly Guid _exerciseId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void RoutineDraft_Create_WithValidData_CreatesEntitySuccessfully()
    {
        // Arrange
        var exercises = new List<SuggestedExerciseDto>
        {
            new("Sentadilla con Barra", 4, 8, 80m, _exerciseId)
        };
        var exercisesJson = JsonSerializer.Serialize(exercises);

        // Act
        var draft = RoutineDraft.Create(
            athleteId: _athleteId,
            goal: "Fuerza Máxima de Piernas",
            proposedTitle: "Rutina Fuerza Cuádriceps",
            justificationSummary: "Priorización de sentadilla pesada y trabajo accesorio",
            providerUsed: "Ollama",
            exercisesJson: exercisesJson
        );

        // Assert
        Assert.NotEqual(Guid.Empty, draft.Id);
        Assert.Equal(_athleteId, draft.AthleteId);
        Assert.Equal("Fuerza Máxima de Piernas", draft.Goal);
        Assert.Equal("Rutina Fuerza Cuádriceps", draft.ProposedTitle);
        Assert.Equal("Priorización de sentadilla pesada y trabajo accesorio", draft.JustificationSummary);
        Assert.Equal("Ollama", draft.ProviderUsed);
        Assert.Equal(exercisesJson, draft.ExercisesJson);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RoutineDraft_Create_WithEmptyGoal_ThrowsDomainException(string invalidGoal)
    {
        // Act & Assert
        var ex = Assert.Throws<DomainException>(() =>
            RoutineDraft.Create(_athleteId, invalidGoal, "Título", "Justificación", "Ollama", "[]"));

        Assert.Contains("El objetivo de entrenamiento es obligatorio", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RoutineDraft_Create_WithEmptyTitle_ThrowsDomainException(string invalidTitle)
    {
        // Act & Assert
        var ex = Assert.Throws<DomainException>(() =>
            RoutineDraft.Create(_athleteId, "Objetivo", invalidTitle, "Justificación", "Ollama", "[]"));

        Assert.Contains("El título propuesto es obligatorio", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RoutineDraft_Create_WithEmptyJustification_ThrowsDomainException(string invalidJustification)
    {
        // Act & Assert
        var ex = Assert.Throws<DomainException>(() =>
            RoutineDraft.Create(_athleteId, "Objetivo", "Título", invalidJustification, "Ollama", "[]"));

        Assert.Contains("El resumen de justificación es obligatorio", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RoutineDraft_Create_WithEmptyExercisesJson_ThrowsDomainException(string invalidJson)
    {
        // Act & Assert
        var ex = Assert.Throws<DomainException>(() =>
            RoutineDraft.Create(_athleteId, "Objetivo", "Título", "Justificación", "Ollama", invalidJson));

        Assert.Contains("El borrador debe contener al menos un ejercicio en formato JSON", ex.Message);
    }

    [Fact]
    public void RoutineDraft_Create_WithEmptyAthleteId_ThrowsDomainException()
    {
        // Act & Assert
        var ex = Assert.Throws<DomainException>(() =>
            RoutineDraft.Create(Guid.Empty, "Objetivo", "Título", "Justificación", "Ollama", "[{}]"));

        Assert.Contains("El identificador del atleta es obligatorio", ex.Message);
    }

    [Fact]
    public async Task SuggestRoutineUseCase_WhenAiGeneratesRoutine_PersistsDraftAndReturnsDtoWithDraftId()
    {
        // Arrange
        var request = new RoutineSuggestionRequestDto(
            AthleteId: _athleteId,
            Goal: "Fuerza de Piernas",
            MuscleGroupEmphasis: ["Piernas"]
        );

        var exercises = new List<SuggestedExerciseDto>
        {
            new("Sentadilla Frontal", 4, 6, 75m, _exerciseId),
            new("Prensa de Piernas", 3, 10, 70m)
        };

        var aiResponse = new RoutineSuggestionResponseDto(
            AthleteId: _athleteId,
            ProposedTitle: "Propuesta de Fuerza",
            JustificationSummary: "Cargas progresivas en tren inferior",
            Exercises: exercises,
            ProviderUsed: "Ollama"
        );

        var aiServiceMock = new Mock<IAiService>();
        aiServiceMock
            .Setup(s => s.SuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        RoutineDraft? capturedDraft = null;
        var repositoryMock = new Mock<IRoutineDraftRepository>();
        repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<RoutineDraft>(), It.IsAny<CancellationToken>()))
            .Callback<RoutineDraft, CancellationToken>((d, _) => capturedDraft = d)
            .Returns(Task.CompletedTask);

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var useCase = new SuggestRoutineUseCase(
            aiService: aiServiceMock.Object,
            routineDraftRepository: repositoryMock.Object,
            unitOfWork: unitOfWorkMock.Object
        );

        // Act
        var result = await useCase.ExecuteAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(aiResponse.ProposedTitle, result.ProposedTitle);
        Assert.Equal(aiResponse.JustificationSummary, result.JustificationSummary);
        Assert.Equal(aiResponse.ProviderUsed, result.ProviderUsed);
        Assert.NotNull(result.Id);
        Assert.NotEqual(Guid.Empty, result.Id);

        repositoryMock.Verify(r => r.AddAsync(It.IsAny<RoutineDraft>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        Assert.NotNull(capturedDraft);
        Assert.Equal(result.Id, capturedDraft.Id);
        Assert.Equal(_athleteId, capturedDraft.AthleteId);
        Assert.Equal("Fuerza de Piernas", capturedDraft.Goal);
        Assert.Equal("Ollama", capturedDraft.ProviderUsed);
    }

    [Fact]
    public async Task SuggestRoutineUseCase_WhenPersistenceThrows_DoesNotCrashAndReturnsAiResponse()
    {
        // Arrange
        var request = new RoutineSuggestionRequestDto(
            AthleteId: _athleteId,
            Goal: "Potencia"
        );

        var aiResponse = new RoutineSuggestionResponseDto(
            AthleteId: _athleteId,
            ProposedTitle: "Rutina Potencia",
            JustificationSummary: "Movimientos explosivos",
            Exercises: [new SuggestedExerciseDto("Salto al cajón", 3, 5, 0m)],
            ProviderUsed: "Gemini"
        );

        var aiServiceMock = new Mock<IAiService>();
        aiServiceMock
            .Setup(s => s.SuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        var repositoryMock = new Mock<IRoutineDraftRepository>();
        repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<RoutineDraft>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB timeout"));

        var unitOfWorkMock = new Mock<IUnitOfWork>();

        var useCase = new SuggestRoutineUseCase(
            aiService: aiServiceMock.Object,
            routineDraftRepository: repositoryMock.Object,
            unitOfWork: unitOfWorkMock.Object
        );

        // Act
        var result = await useCase.ExecuteAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Rutina Potencia", result.ProposedTitle);
        Assert.Equal("Gemini", result.ProviderUsed);
    }
}
