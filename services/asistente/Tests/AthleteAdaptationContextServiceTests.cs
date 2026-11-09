using Application.DTOs.Adaptation;
using Application.DTOs.DayAdjustments;
using Application.DTOs.DayConflicts;
using Application.Interfaces.Services;
using Application.UseCases.Adaptation.GetAthleteAdaptationContext;
using Application.Exceptions;
using Infrastructure.Services.Adaptation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Asistente.Tests;

public sealed class AthleteAdaptationContextServiceTests
{
    [Fact]
    public async Task Given_ActiveInjuryAndCurrentRecord_When_ContextIsRequested_Then_CombinesAuthorizedData()
    {
        var athleteId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();
        var substituteId = Guid.NewGuid();
        var seguimiento = new Mock<ISeguimientoClient>();
        var training = new Mock<ITrainingPlanServiceClient>();

        seguimiento
            .Setup(client => client.GetActiveInjuriesAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ActiveInjuryRemoteDto(
                    Guid.NewGuid(),
                    athleteId,
                    "rodilla",
                    "Dolor rotuliano",
                    DateTime.UtcNow.AddDays(-2),
                    DateTime.UtcNow.AddDays(5),
                    "Active",
                    null,
                    DateTime.UtcNow.AddDays(-2))
            ]);
        training
            .Setup(client => client.GetCurrentRecordsAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AthleteRecordRemoteDto(
                    Guid.NewGuid(),
                    athleteId,
                    exerciseId,
                    "Sentadilla con barra",
                    120m,
                    "Estimated",
                    DateTime.UtcNow.AddDays(-1),
                    true)
            ]);
        training
            .Setup(client => client.CheckExerciseRestrictionsAsync(
                "rodilla:3",
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { exerciseId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ExerciseRestrictionCheckResultRemoteDto(
                    exerciseId,
                    true,
                    "rodilla",
                    3,
                    "Reduce estrés rotuliano",
                    [new DayConflictSubstituteDto(substituteId, "Prensa de piernas")])
            ]);

        var service = new AthleteAdaptationContextService(
            seguimiento.Object,
            training.Object,
            NullLogger<AthleteAdaptationContextService>.Instance);

        var result = await service.GetAsync(athleteId);

        Assert.Equal(athleteId, result.AthleteId);
        Assert.Single(result.ActiveInjuries);
        var currentRecord = Assert.Single(result.CurrentRecords);
        Assert.Equal(exerciseId, currentRecord.ExerciseId);
        Assert.Equal(120m, currentRecord.WeightOneRepMaxKg);
        var restriction = Assert.Single(result.Restrictions);
        Assert.Equal(exerciseId, restriction.ExerciseId);
        Assert.Equal(substituteId, Assert.Single(restriction.Substitutes).ExerciseId);
        training.Verify(client => client.CheckExerciseRestrictionsAsync(
            "rodilla:3",
            It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { exerciseId })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_InjuriesAndRecords_When_ContextIsRequested_Then_StartsBothQueriesBeforeWaitingForResults()
    {
        var athleteId = Guid.NewGuid();
        var injuriesStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recordsStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseQueries = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var seguimiento = new Mock<ISeguimientoClient>();
        var training = new Mock<ITrainingPlanServiceClient>();

        seguimiento
            .Setup(client => client.GetActiveInjuriesAsync(athleteId, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                injuriesStarted.SetResult(true);
                await releaseQueries.Task;
                return (IReadOnlyList<ActiveInjuryRemoteDto>)[];
            });
        training
            .Setup(client => client.GetCurrentRecordsAsync(athleteId, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                recordsStarted.SetResult(true);
                await releaseQueries.Task;
                return (IReadOnlyList<AthleteRecordRemoteDto>)[];
            });

        var service = new AthleteAdaptationContextService(
            seguimiento.Object,
            training.Object,
            NullLogger<AthleteAdaptationContextService>.Instance);

        var contextTask = service.GetAsync(athleteId);
        await Task.WhenAll(injuriesStarted.Task, recordsStarted.Task);

        Assert.False(contextTask.IsCompleted);
        releaseQueries.SetResult(true);

        var result = await contextTask;

        Assert.Empty(result.ActiveInjuries);
        Assert.Empty(result.CurrentRecords);
        Assert.Empty(result.Restrictions);
    }

    [Fact]
    public async Task Given_InactiveInjuryAndHistoricalRecord_When_ContextIsRequested_Then_ExcludesThem()
    {
        var athleteId = Guid.NewGuid();
        var seguimiento = new Mock<ISeguimientoClient>();
        var training = new Mock<ITrainingPlanServiceClient>();

        seguimiento
            .Setup(client => client.GetActiveInjuriesAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ActiveInjuryRemoteDto(
                    Guid.NewGuid(),
                    athleteId,
                    "rodilla",
                    "Resuelta",
                    DateTime.UtcNow.AddDays(-10),
                    DateTime.UtcNow.AddDays(-2),
                    "MedicallyCleared",
                    DateTime.UtcNow.AddDays(-2),
                    DateTime.UtcNow.AddDays(-10))
            ]);
        training
            .Setup(client => client.GetCurrentRecordsAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AthleteRecordRemoteDto(
                    Guid.NewGuid(),
                    athleteId,
                    Guid.NewGuid(),
                    "Sentadilla",
                    100m,
                    "Tested",
                    DateTime.UtcNow.AddDays(-30),
                    false)
            ]);

        var service = new AthleteAdaptationContextService(
            seguimiento.Object,
            training.Object,
            NullLogger<AthleteAdaptationContextService>.Instance);

        var result = await service.GetAsync(athleteId);

        Assert.Empty(result.ActiveInjuries);
        Assert.Empty(result.CurrentRecords);
        Assert.Empty(result.Restrictions);
        training.Verify(
            client => client.CheckExerciseRestrictionsAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Given_ActiveInjuryWithoutCurrentRecords_When_ContextIsRequested_Then_DoesNotCheckRestrictions()
    {
        var athleteId = Guid.NewGuid();
        var seguimiento = new Mock<ISeguimientoClient>();
        var training = new Mock<ITrainingPlanServiceClient>();

        seguimiento
            .Setup(client => client.GetActiveInjuriesAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ActiveInjuryRemoteDto(
                    Guid.NewGuid(),
                    athleteId,
                    "rodilla",
                    "Dolor",
                    DateTime.UtcNow.AddDays(-1),
                    DateTime.UtcNow.AddDays(5),
                    "Active",
                    null,
                    DateTime.UtcNow.AddDays(-1))
            ]);
        training
            .Setup(client => client.GetCurrentRecordsAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = new AthleteAdaptationContextService(
            seguimiento.Object,
            training.Object,
            NullLogger<AthleteAdaptationContextService>.Instance);

        var result = await service.GetAsync(athleteId);

        Assert.Single(result.ActiveInjuries);
        Assert.Empty(result.CurrentRecords);
        Assert.Empty(result.Restrictions);
        training.Verify(
            client => client.CheckExerciseRestrictionsAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Given_EmptyAthleteId_When_ContextIsRequested_Then_Throws()
    {
        var service = new AthleteAdaptationContextService(
            Mock.Of<ISeguimientoClient>(),
            Mock.Of<ITrainingPlanServiceClient>(),
            NullLogger<AthleteAdaptationContextService>.Instance);

        var action = () => service.GetAsync(Guid.Empty);

        await Assert.ThrowsAsync<ArgumentException>(action);
    }

    [Fact]
    public async Task Given_OwnAthlete_When_HandlerIsUsed_Then_ReturnsContext()
    {
        var athleteId = Guid.NewGuid();
        var context = new AthleteAdaptationContextDto(athleteId, [], [], []);
        var contextService = new Mock<IAthleteAdaptationContextService>();
        var structureClient = new Mock<IStructureAthleteClient>();
        structureClient.Setup(client => client.IsOwnAthleteAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        contextService.Setup(service => service.GetAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        var handler = new GetAthleteAdaptationContextQueryHandler(contextService.Object, structureClient.Object);

        var result = await handler.HandleAsync(new GetAthleteAdaptationContextQuery(athleteId));

        Assert.Same(context, result);
        contextService.Verify(service => service.GetAsync(athleteId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_ForeignAthlete_When_HandlerIsUsed_Then_ThrowsNotFoundAndDoesNotQueryContext()
    {
        var athleteId = Guid.NewGuid();
        var contextService = new Mock<IAthleteAdaptationContextService>();
        var structureClient = new Mock<IStructureAthleteClient>();
        structureClient.Setup(client => client.IsOwnAthleteAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new GetAthleteAdaptationContextQueryHandler(contextService.Object, structureClient.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new GetAthleteAdaptationContextQuery(athleteId)));

        contextService.Verify(
            service => service.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Given_EmptyAthleteId_When_HandlerIsUsed_Then_ThrowsNotFoundWithoutCallingStructure()
    {
        var structureClient = new Mock<IStructureAthleteClient>();
        var handler = new GetAthleteAdaptationContextQueryHandler(
            Mock.Of<IAthleteAdaptationContextService>(),
            structureClient.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new GetAthleteAdaptationContextQuery(Guid.Empty)));

        structureClient.Verify(
            client => client.IsOwnAthleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
