namespace Asistente.Tests;

using System.Reflection;
using Application.DTOs.Adaptation;
using Application.DTOs.Ai;
using Application.DTOs.DayConflicts;
using Application.Interfaces.Services;
using Application.UseCases.Ai.SuggestRoutine;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using API.Controllers;

public sealed class AthleteAdaptationUseCaseSecurityTests
{
    [Fact]
    public async Task Given_AthleteNotOwnedByPf_When_AdaptationIsRequested_Then_RejectsBeforeReadingContext()
    {
        var athleteId = Guid.NewGuid();
        var structure = new Mock<IStructureAthleteClient>();
        var context = new Mock<IAthleteAdaptationContextService>();
        var ai = new Mock<IAiService>();
        structure
            .Setup(client => client.IsOwnAthleteAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var useCase = new SuggestRoutineUseCase(
            ai.Object,
            context.Object,
            structure.Object);

        await Assert.ThrowsAsync<Application.Exceptions.NotFoundException>(() =>
            useCase.ExecuteAdaptationAsync(
                new RoutineSuggestionRequestDto(athleteId, "Fuerza"),
                CancellationToken.None));

        context.Verify(
            service => service.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        ai.Verify(
            service => service.SuggestRoutineAsync(
                It.IsAny<RoutineSuggestionRequestDto>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Given_AdaptationEndpoint_When_InspectingMetadata_Then_DocumentsUnauthorizedResponse()
    {
        var method = typeof(AssistantController).GetMethod(nameof(AssistantController.GenerateAthleteAdaptation));

        Assert.NotNull(method);
        Assert.Contains(
            method.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task Given_LucasWithKneeInjuryAndBaseRoutine_When_AdaptationIsGenerated_Then_ReturnsBaseVsAdaptedDiffWithLegPressAndMedicalReason()
    {
        var lucasId = Guid.NewGuid();
        var squatId = Guid.NewGuid();
        var benchId = Guid.NewGuid();
        var legPressId = Guid.NewGuid();

        var structure = new Mock<IStructureAthleteClient>();
        structure
            .Setup(client => client.IsOwnAthleteAsync(lucasId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var contextService = new Mock<IAthleteAdaptationContextService>();
        contextService
            .Setup(service => service.GetAsync(lucasId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteAdaptationContextDto(
                lucasId,
                [
                    new ActiveInjuryRemoteDto(
                        Guid.NewGuid(),
                        lucasId,
                        "rodilla",
                        "Dolor en tendón rotuliano",
                        DateTime.UtcNow.AddDays(-3),
                        DateTime.UtcNow.AddDays(7),
                        "Active",
                        null,
                        DateTime.UtcNow.AddDays(-3))
                ],
                [],
                [
                    new ExerciseRestrictionContextDto(
                        squatId,
                        "rodilla",
                        "Reduce estrés en tendón rotuliano",
                        [new AuthorizedSubstituteDto(legPressId, "Prensa de piernas")])
                ]));

        var useCase = new SuggestRoutineUseCase(
            Mock.Of<IAiService>(),
            contextService.Object,
            structure.Object);

        var baseExercises = new List<SuggestedExerciseDto>
        {
            new("Sentadilla con barra", 4, 5, 75m, squatId),
            new("Press de banca con barra - Agarre medio", 4, 6, 70m, benchId)
        };

        var result = await useCase.ExecuteAdaptationAsync(
            new RoutineSuggestionRequestDto(
                AthleteId: lucasId,
                Goal: "Base de fuerza para la pretemporada",
                BaseExercises: baseExercises));

        Assert.True(result.WasAdapted);
        Assert.NotNull(result.BaseExercises);
        Assert.Equal(2, result.BaseExercises!.Count);
        Assert.Equal("Sentadilla con barra", result.BaseExercises[0].ExerciseName);
        Assert.Equal("Press de banca con barra - Agarre medio", result.BaseExercises[1].ExerciseName);

        Assert.Equal(2, result.Exercises.Count);
        Assert.Equal(legPressId, result.Exercises[0].ExerciseId);
        Assert.Equal("Prensa de piernas", result.Exercises[0].ExerciseName);
        Assert.Equal(benchId, result.Exercises[1].ExerciseId);
        Assert.Equal("Press de banca con barra - Agarre medio", result.Exercises[1].ExerciseName);

        var change = Assert.Single(result.AdaptationChanges!);
        Assert.Equal(squatId, change.OriginalExerciseId);
        Assert.Equal("Sentadilla con barra", change.OriginalExerciseName);
        Assert.Equal(legPressId, change.FinalExerciseId);
        Assert.Equal("Prensa de piernas", change.FinalExerciseName);
        Assert.Equal("rodilla", change.Zone);
        Assert.Equal(
            "Sustitución biomecánica por lesión de rodilla para reducir estrés en tendón rotuliano. Sustituto autorizado: Prensa de piernas.",
            change.Reason);
        Assert.NotNull(change.AvailableSubstitutes);
        Assert.Single(change.AvailableSubstitutes!);
    }

    [Fact]
    public async Task Given_LucasWithoutSquatRecordAndWithActiveInjury_When_BaseRoutineAdaptationIsRequested_Then_ChecksRestrictionsForBaseExercises()
    {
        var lucasId = Guid.NewGuid();
        var squatId = Guid.NewGuid();
        var legPressId = Guid.NewGuid();

        var structure = new Mock<IStructureAthleteClient>();
        structure
            .Setup(client => client.IsOwnAthleteAsync(lucasId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var contextService = new Mock<IAthleteAdaptationContextService>();
        contextService
            .Setup(service => service.GetAsync(lucasId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteAdaptationContextDto(
                lucasId,
                [
                    new ActiveInjuryRemoteDto(
                        Guid.NewGuid(),
                        lucasId,
                        "rodilla",
                        "Molestia rotuliana",
                        DateTime.UtcNow.AddDays(-2),
                        DateTime.UtcNow.AddDays(10),
                        "Active",
                        null,
                        DateTime.UtcNow.AddDays(-2))
                ],
                [],
                []));

        var trainingClient = new Mock<ITrainingPlanServiceClient>();
        trainingClient
            .Setup(client => client.CheckExerciseRestrictionsAsync(
                It.Is<string>(z => z.Contains("cuadriceps:3") && z.Contains("rodilla:3")),
                It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(squatId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ExerciseRestrictionCheckResultRemoteDto(
                    squatId,
                    true,
                    "cuadriceps",
                    3,
                    "Alta compresión femororrotuliana y tensión en el tendón rotuliano",
                    [new DayConflictSubstituteDto(legPressId, "Prensa de piernas")])
            ]);

        var useCase = new SuggestRoutineUseCase(
            Mock.Of<IAiService>(),
            contextService.Object,
            structure.Object,
            trainingPlanServiceClient: trainingClient.Object);

        var result = await useCase.ExecuteAdaptationAsync(
            new RoutineSuggestionRequestDto(
                AthleteId: lucasId,
                Goal: "Fuerza base",
                BaseExercises: [new SuggestedExerciseDto("Sentadilla con barra", 4, 5, 75m, squatId)]));

        Assert.True(result.WasAdapted);
        Assert.Equal("Sentadilla con barra", Assert.Single(result.BaseExercises!).ExerciseName);
        Assert.Equal("Prensa de piernas", Assert.Single(result.Exercises).ExerciseName);
        var change = Assert.Single(result.AdaptationChanges!);
        Assert.Equal("rodilla", change.Zone);
        Assert.Equal("Prensa de piernas", change.FinalExerciseName);
    }

    [Fact]
    public async Task Given_AthleteWithEstimatedRecord_When_BaseRoutineAdaptationIsGenerated_Then_IncludesEstimatedRecordAlertWarning()
    {
        var lucasId = Guid.NewGuid();
        var squatId = Guid.NewGuid();
        var benchId = Guid.NewGuid();
        var legPressId = Guid.NewGuid();

        var structure = new Mock<IStructureAthleteClient>();
        structure
            .Setup(client => client.IsOwnAthleteAsync(lucasId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var contextService = new Mock<IAthleteAdaptationContextService>();
        contextService
            .Setup(service => service.GetAsync(lucasId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteAdaptationContextDto(
                lucasId,
                [
                    new ActiveInjuryRemoteDto(
                        Guid.NewGuid(),
                        lucasId,
                        "rodilla",
                        "Dolor en tendón rotuliano",
                        DateTime.UtcNow.AddDays(-3),
                        DateTime.UtcNow.AddDays(7),
                        "Active",
                        null,
                        DateTime.UtcNow.AddDays(-3))
                ],
                [
                    new AthleteRecordRemoteDto(
                        Guid.NewGuid(),
                        lucasId,
                        squatId,
                        "Sentadilla con barra",
                        110m,
                        "EstimatedEpley",
                        DateTime.UtcNow.AddDays(-5),
                        true),
                    new AthleteRecordRemoteDto(
                        Guid.NewGuid(),
                        lucasId,
                        benchId,
                        "Press de banca con barra - Agarre medio",
                        85m,
                        "Estimated",
                        DateTime.UtcNow.AddDays(-5),
                        true)
                ],
                [
                    new ExerciseRestrictionContextDto(
                        squatId,
                        "rodilla",
                        "Reduce estrés en tendón rotuliano",
                        [new AuthorizedSubstituteDto(legPressId, "Prensa de piernas")])
                ]));

        var useCase = new SuggestRoutineUseCase(
            Mock.Of<IAiService>(),
            contextService.Object,
            structure.Object);

        var result = await useCase.ExecuteAdaptationAsync(
            new RoutineSuggestionRequestDto(
                AthleteId: lucasId,
                Goal: "Fuerza base",
                BaseExercises:
                [
                    new SuggestedExerciseDto("Sentadilla con barra", 4, 5, 75m, squatId),
                    new SuggestedExerciseDto("Press de banca con barra - Agarre medio", 4, 6, 70m, benchId)
                ]));

        Assert.True(result.WasAdapted);
        Assert.NotNull(result.AdaptationWarnings);
        Assert.Contains(
            result.AdaptationWarnings!,
            warning => warning.Contains("Alerta de dato estimado", StringComparison.OrdinalIgnoreCase) &&
                       warning.Contains("Sentadilla con barra", StringComparison.OrdinalIgnoreCase) &&
                       warning.Contains("110 kg", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            result.AdaptationWarnings!,
            warning => warning.Contains("Alerta de dato estimado", StringComparison.OrdinalIgnoreCase) &&
                       warning.Contains("Press de banca con barra - Agarre medio", StringComparison.OrdinalIgnoreCase) &&
                       warning.Contains("85 kg", StringComparison.OrdinalIgnoreCase));
    }
}
