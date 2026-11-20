using Application.Interfaces.Persistence;
using Application.UseCases.Injuries.GetActiveInjuriesSummary;
using Moq;
using Xunit;

namespace Tests;

public sealed class GetActiveInjuriesSummaryQueryHandlerTests
{
    private readonly Mock<IAthleteInjuryRepository> _repository = new();
    private readonly GetActiveInjuriesSummaryQueryHandler _handler;

    public GetActiveInjuriesSummaryQueryHandlerTests()
    {
        _handler = new GetActiveInjuriesSummaryQueryHandler(_repository.Object);
    }

    [Fact]
    public async Task Given_EmptyAthleteIds_When_Handled_Then_ReturnsEmptyWithoutQueryingRepository()
    {
        var result = await _handler.HandleAsync(new GetActiveInjuriesSummaryQuery([]));

        Assert.Empty(result);
        _repository.Verify(
            r => r.CountActiveByAthleteIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Given_MultipleAthletesWithDuplicates_When_Handled_Then_QueriesOnceAndReturnsZeroForHealthyAthletes()
    {
        var injuredId = Guid.NewGuid();
        var healthyId = Guid.NewGuid();

        _repository
            .Setup(r => r.CountActiveByAthleteIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(injuredId) && ids.Contains(healthyId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [injuredId] = 2 });

        var result = await _handler.HandleAsync(
            new GetActiveInjuriesSummaryQuery([injuredId, healthyId, injuredId, Guid.Empty]));

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result.Single(item => item.AthleteId == injuredId).ActiveCount);
        Assert.Equal(0, result.Single(item => item.AthleteId == healthyId).ActiveCount);
        _repository.Verify(
            r => r.CountActiveByAthleteIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
