using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.UseCases.FieldTests.GetFieldTestSession;
using Domain.Entities;
using Moq;

namespace Seguimiento.Tests;

// KAN-425: el GET devuelve la sesión con sus tests ordenados y sus resultados; una
// sesión inexistente o de otro PF (invisible por el filtro global) → 404.
public class GetFieldTestSessionQueryHandlerTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    private readonly Mock<IFieldTestRepository> _fieldTests = new();

    private readonly FieldTestSession _session = new()
    {
        CategoryId = Guid.NewGuid(),
        Date = new DateTime(2026, 11, 4),
        Name = "Evaluación de pretemporada",
        Status = FieldTestSessionStatus.InProgress
    };

    public GetFieldTestSessionQueryHandlerTests()
    {
        _fieldTests
            .Setup(repo => repo.GetSessionByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_session);
    }

    private GetFieldTestSessionQueryHandler CreateHandler() => new(_fieldTests.Object);

    [Fact]
    public async Task HandleAsync_WithExistingSession_ReturnsSessionWithTestsAndResults()
    {
        var testId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        var loadedResult = new FieldTestResult
        {
            SessionId = SessionId,
            CatalogTestId = testId,
            AthleteId = athleteId,
            Value = 42.5m,
            Attempt = 1,
            RecordedAt = DateTime.UtcNow
        };
        _fieldTests
            .Setup(repo => repo.ListSessionTestsAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FieldTestSessionTest>
            {
                new() { SessionId = SessionId, CatalogTestId = testId, Order = 1 }
            });
        _fieldTests
            .Setup(repo => repo.ListResultsBySessionAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FieldTestResult> { loadedResult });

        var dto = await CreateHandler().HandleAsync(new GetFieldTestSessionQuery(SessionId));

        Assert.Equal(_session.Id, dto.Id);
        Assert.Equal(_session.CategoryId, dto.CategoryId);
        Assert.Equal("Evaluación de pretemporada", dto.Name);
        Assert.Equal("InProgress", dto.Status);

        var test = Assert.Single(dto.Tests);
        Assert.Equal(testId, test.CatalogTestId);
        Assert.Equal(1, test.Order);

        var result = Assert.Single(dto.Results);
        Assert.Equal(loadedResult.Id, result.Id);
        Assert.Equal(athleteId, result.AthleteId);
        Assert.Equal(42.5m, result.Value);
        Assert.Equal(1, result.Attempt);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownSession_ThrowsSessionNotFoundWithoutQueryingResults()
    {
        _fieldTests
            .Setup(repo => repo.GetSessionByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FieldTestSession?)null);

        await Assert.ThrowsAsync<FieldTestSessionNotFoundException>(
            () => CreateHandler().HandleAsync(new GetFieldTestSessionQuery(Guid.NewGuid())));

        _fieldTests.Verify(
            repo => repo.ListResultsBySessionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
