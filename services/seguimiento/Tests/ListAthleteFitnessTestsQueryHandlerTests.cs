using Application.DTOs.FitnessTests;
using Application.Interfaces.Persistence;
using Application.UseCases.FitnessTests.ListAthleteFitnessTests;
using Moq;
using Xunit;

namespace Seguimiento.Tests;

public class ListAthleteFitnessTestsQueryHandlerTests
{
    private readonly Mock<IFieldTestResultRepository> _repository = new();

    [Fact]
    public async Task Handle_ConFiltros_LlamaRepoConFechasUtc()
    {
        var athleteId = Guid.NewGuid();
        var testId = Guid.NewGuid();
        var from = new DateOnly(2026, 3, 1);
        var to = new DateOnly(2026, 3, 31);

        _repository
            .Setup(r => r.ListByAthleteAndDateRangeAsync(
                athleteId,
                testId,
                It.Is<DateTime?>(d => d == from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
                It.Is<DateTime?>(d => d == to.ToDateTime(new TimeOnly(23, 59, 59, 999), DateTimeKind.Utc)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AthleteFitnessTestDto>());

        var handler = new ListAthleteFitnessTestsQueryHandler(_repository.Object);
        var result = await handler.HandleAsync(new ListAthleteFitnessTestsQuery(athleteId, testId, from, to));

        Assert.Empty(result);
        _repository.VerifyAll();
    }

    [Fact]
    public async Task Handle_ConResultados_DevuelveLista()
    {
        var athleteId = Guid.NewGuid();
        var testId = Guid.NewGuid();
        var testDto = new AthleteFitnessTestDto(
            Guid.NewGuid(),
            athleteId,
            testId,
            "Salto Vertical CMJ",
            "Power",
            45.5m,
            "cm",
            1,
            new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc));

        _repository
            .Setup(r => r.ListByAthleteAndDateRangeAsync(
                athleteId,
                null,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AthleteFitnessTestDto> { testDto });

        var handler = new ListAthleteFitnessTestsQueryHandler(_repository.Object);
        var result = await handler.HandleAsync(new ListAthleteFitnessTestsQuery(athleteId));

        var item = Assert.Single(result);
        Assert.Equal(athleteId, item.AthleteId);
        Assert.Equal("Salto Vertical CMJ", item.TestName);
        Assert.Equal(45.5m, item.Value);
        Assert.Equal("cm", item.MeasurementUnit);
    }
}

