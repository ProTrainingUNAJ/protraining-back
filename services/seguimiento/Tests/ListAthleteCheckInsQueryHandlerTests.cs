using Application.DTOs.CheckIns;
using Application.Interfaces.Persistence;
using Application.UseCases.CheckIns.ListAthleteCheckIns;
using Domain.Entities;
using Moq;
using Xunit;

namespace Seguimiento.Tests;

public class ListAthleteCheckInsQueryHandlerTests
{
    private readonly Mock<ICheckInRepository> _repository = new();

    [Fact]
    public async Task Handle_ConRangoFechas_LlamaRepoConFechasUtc()
    {
        var athleteId = Guid.NewGuid();
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 1, 15);

        _repository
            .Setup(r => r.GetByAthleteAndDateRangeAsync(
                athleteId,
                It.Is<DateTime?>(d => d == from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
                It.Is<DateTime?>(d => d == to.ToDateTime(new TimeOnly(23, 59, 59, 999), DateTimeKind.Utc)),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CheckIn>());

        var handler = new ListAthleteCheckInsQueryHandler(_repository.Object);
        var result = await handler.HandleAsync(new ListAthleteCheckInsQuery(athleteId, from, to));

        Assert.Empty(result);
        _repository.VerifyAll();
    }

    [Fact]
    public async Task Handle_SinFechas_AplicaLimiteDefault14()
    {
        var athleteId = Guid.NewGuid();

        _repository
            .Setup(r => r.GetByAthleteAndDateRangeAsync(
                athleteId,
                null,
                null,
                14,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CheckIn>());

        var handler = new ListAthleteCheckInsQueryHandler(_repository.Object);
        var result = await handler.HandleAsync(new ListAthleteCheckInsQuery(athleteId));

        Assert.Empty(result);
        _repository.VerifyAll();
    }

    [Fact]
    public async Task Handle_ConCheckIns_MapeaADto()
    {
        var athleteId = Guid.NewGuid();
        var date = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
        var checkIn = new CheckIn
        {
            AthleteId = athleteId,
            Date = date,
            TrafficLight = TrafficLight.Green,
            SleepHours = 8.0,
            SleepQuality = 4,
            MuscleSoreness = 1,
            FatigueLevel = 2,
            MoodRate = 5,
            StressLevel = 1,
            IsSick = false
        };

        _repository
            .Setup(r => r.GetByAthleteAndDateRangeAsync(
                athleteId,
                null,
                null,
                14,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CheckIn> { checkIn });

        var handler = new ListAthleteCheckInsQueryHandler(_repository.Object);
        var result = await handler.HandleAsync(new ListAthleteCheckInsQuery(athleteId));

        var item = Assert.Single(result);
        Assert.Equal(athleteId, item.AthleteId);
        Assert.Equal(date, item.Date);
        Assert.Equal("Green", item.TrafficLight);
        Assert.Equal(8.0, item.SleepHours);
    }
}

