using Application.DTOs.Evolution;
using Application.DTOs.FitnessTests;
using Application.DTOs.Training;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Evolution.GetAthleteEvolution;
using Domain.Entities;
using Moq;
using Xunit;

namespace Seguimiento.Tests;

public class GetAthleteEvolutionQueryHandlerTests
{
    private readonly Mock<ITrainingClient> _trainingClient = new();
    private readonly Mock<IFieldTestResultRepository> _fieldTestRepo = new();
    private readonly Mock<ICheckInRepository> _checkInRepo = new();
    private readonly Mock<IAthleteAccessGuard> _athleteAccessGuard = new();

    private GetAthleteEvolutionQueryHandler CreateHandler() =>
        new(
            _trainingClient.Object,
            _fieldTestRepo.Object,
            _checkInRepo.Object,
            _athleteAccessGuard.Object);

    [Fact]
    public async Task HandleAsync_ConRangoPorDefecto_CalculaUltimos3MesesYUnificaCurvas()
    {
        var athleteId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();
        var catalogTestId = Guid.NewGuid();

        var recordDto = new AthleteRecordRemoteDto(
            Guid.NewGuid(),
            athleteId,
            exerciseId,
            "Sentadilla Trasera",
            140.0m,
            "Tested",
            new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc),
            true);

        var fitnessTestDto = new AthleteFitnessTestDto(
            Guid.NewGuid(),
            athleteId,
            catalogTestId,
            "Salto Vertical CMJ",
            "Power",
            45.5m,
            "cm",
            1,
            new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc));

        var checkIn = new CheckIn
        {
            AthleteId = athleteId,
            Date = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc),
            FatigueLevel = 3,
            SleepHours = 8.0,
            SleepQuality = 4,
            MuscleSoreness = 2,
            MoodRate = 4,
            StressLevel = 2,
            TrafficLight = TrafficLight.Green,
            HasActiveInjury = false
        };

        _trainingClient
            .Setup(c => c.GetAthleteRecordsAsync(athleteId, It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrainingRecordsResultDto(new List<AthleteRecordRemoteDto> { recordDto }, IsUnavailable: false));

        _fieldTestRepo
            .Setup(r => r.ListByAthleteAndDateRangeAsync(athleteId, null, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AthleteFitnessTestDto> { fitnessTestDto });

        _checkInRepo
            .Setup(r => r.GetByAthleteAndDateRangeAsync(athleteId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CheckIn> { checkIn });

        var handler = CreateHandler();

        var result = await handler.HandleAsync(new GetAthleteEvolutionQuery(athleteId));

        Assert.Equal(athleteId, result.AthleteId);
        Assert.True(result.From < result.To);
        Assert.False(result.StrengthUnavailable);

        // Fuerza
        Assert.Single(result.Strength);
        var strengthSeries = result.Strength[0];
        Assert.Equal("Sentadilla Trasera", strengthSeries.ExerciseName);
        Assert.Single(strengthSeries.Points);
        Assert.Equal(140.0, strengthSeries.Points[0].Value);

        // Salto / Capacidades atléticas
        Assert.Single(result.FitnessTests);
        var testSeries = result.FitnessTests[0];
        Assert.Equal("Salto Vertical CMJ", testSeries.TestName);
        Assert.Single(testSeries.Points);
        Assert.Equal(45.5, testSeries.Points[0].Value);

        // Fatiga / Wellness día a día
        Assert.Single(result.Wellness.FatiguePoints);
        Assert.Equal(3, result.Wellness.FatiguePoints[0].Value);
        Assert.Single(result.Wellness.DailyPoints);
        Assert.Equal(3, result.Wellness.DailyPoints[0].FatigueLevel);
    }

    [Fact]
    public async Task HandleAsync_ConMultiplesIntentosMismoDia_TomaElMejorResultado()
    {
        var athleteId = Guid.NewGuid();
        var jumpTestId = Guid.NewGuid();
        var speedTestId = Guid.NewGuid();
        var date = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);

        var jumpAttempt1 = new AthleteFitnessTestDto(
            Guid.NewGuid(), athleteId, jumpTestId, "CMJ", "Power", 40.0m, "cm", 1, date);
        var jumpAttempt2 = new AthleteFitnessTestDto(
            Guid.NewGuid(), athleteId, jumpTestId, "CMJ", "Power", 43.5m, "cm", 2, date);

        var speedAttempt1 = new AthleteFitnessTestDto(
            Guid.NewGuid(), athleteId, speedTestId, "Sprint 30m", "Speed", 4.10m, "seg", 1, date);
        var speedAttempt2 = new AthleteFitnessTestDto(
            Guid.NewGuid(), athleteId, speedTestId, "Sprint 30m", "Speed", 3.92m, "seg", 2, date);

        _trainingClient
            .Setup(c => c.GetAthleteRecordsAsync(athleteId, It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrainingRecordsResultDto([], IsUnavailable: false));

        _fieldTestRepo
            .Setup(r => r.ListByAthleteAndDateRangeAsync(athleteId, null, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AthleteFitnessTestDto> { jumpAttempt1, jumpAttempt2, speedAttempt1, speedAttempt2 });

        _checkInRepo
            .Setup(r => r.GetByAthleteAndDateRangeAsync(athleteId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CheckIn>());

        var handler = CreateHandler();

        var result = await handler.HandleAsync(new GetAthleteEvolutionQuery(athleteId));

        Assert.False(result.StrengthUnavailable);

        var jumpSeries = result.FitnessTests.First(t => t.TestName == "CMJ");
        Assert.Single(jumpSeries.Points);
        Assert.Equal(43.5, jumpSeries.Points[0].Value);

        var speedSeries = result.FitnessTests.First(t => t.TestName == "Sprint 30m");
        Assert.Single(speedSeries.Points);
        Assert.Equal(3.92, speedSeries.Points[0].Value);
    }

    [Fact]
    public async Task HandleAsync_CuandoEntrenamientoNoTieneDatos_DevuelveStrengthUnavailableFalseYCurvasVacias()
    {
        var athleteId = Guid.NewGuid();

        _trainingClient
            .Setup(c => c.GetAthleteRecordsAsync(athleteId, It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrainingRecordsResultDto([], IsUnavailable: false));

        _fieldTestRepo
            .Setup(r => r.ListByAthleteAndDateRangeAsync(athleteId, null, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AthleteFitnessTestDto>());

        _checkInRepo
            .Setup(r => r.GetByAthleteAndDateRangeAsync(athleteId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CheckIn>());

        var handler = CreateHandler();

        var result = await handler.HandleAsync(new GetAthleteEvolutionQuery(athleteId));

        Assert.NotNull(result);
        Assert.False(result.StrengthUnavailable);
        Assert.Empty(result.Strength);
        Assert.Empty(result.FitnessTests);
        Assert.Empty(result.Wellness.FatiguePoints);
        Assert.Empty(result.Wellness.DailyPoints);
    }

    [Fact]
    public async Task HandleAsync_CuandoEntrenamientoFalla_DevuelveStrengthUnavailableTrue()
    {
        var athleteId = Guid.NewGuid();

        _trainingClient
            .Setup(c => c.GetAthleteRecordsAsync(athleteId, It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrainingRecordsResultDto([], IsUnavailable: true));

        _fieldTestRepo
            .Setup(r => r.ListByAthleteAndDateRangeAsync(athleteId, null, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AthleteFitnessTestDto>());

        _checkInRepo
            .Setup(r => r.GetByAthleteAndDateRangeAsync(athleteId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CheckIn>());

        var handler = CreateHandler();

        var result = await handler.HandleAsync(new GetAthleteEvolutionQuery(athleteId));

        Assert.NotNull(result);
        Assert.True(result.StrengthUnavailable);
        Assert.Empty(result.Strength);
    }

    [Fact]
    public async Task HandleAsync_AtletaDeOtroPf_LanzaAthleteNotFoundException()
    {
        var athleteId = Guid.NewGuid();

        _athleteAccessGuard
            .Setup(g => g.EnsureCanReadAsync(athleteId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AthleteNotFoundException());

        var handler = CreateHandler();

        await Assert.ThrowsAsync<AthleteNotFoundException>(() =>
            handler.HandleAsync(new GetAthleteEvolutionQuery(athleteId)));

        _trainingClient.Verify(
            c => c.GetAthleteRecordsAsync(It.IsAny<Guid>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
