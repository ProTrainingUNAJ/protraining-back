using Application.Interfaces.Persistence;
using Application.UseCases.FieldTests.ListFieldTestSessions;
using Domain.Entities;
using Moq;

namespace Seguimiento.Tests;

// El listado es de solo lectura (sin SaveChanges) y devuelve el resumen de cada
// sesión de la categoría, con el estado como texto.
public class ListFieldTestSessionsQueryHandlerTests
{
    private static readonly Guid CategoryId = Guid.NewGuid();

    private readonly Mock<IFieldTestRepository> _fieldTests = new();

    private ListFieldTestSessionsQueryHandler CreateHandler() => new(_fieldTests.Object);

    [Fact]
    public async Task HandleAsync_WithSessions_ReturnsSummariesMappedFromEntity()
    {
        var sessions = new List<FieldTestSession>
        {
            new()
            {
                CategoryId = CategoryId,
                Date = new DateTime(2026, 11, 9),
                Name = "Evaluación de pretemporada",
                Status = FieldTestSessionStatus.Planned
            },
            new()
            {
                CategoryId = CategoryId,
                Date = new DateTime(2026, 10, 1),
                Name = "Test de arranque",
                Status = FieldTestSessionStatus.Closed
            }
        };

        _fieldTests
            .Setup(repo => repo.ListSessionsByCategoryAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);

        var summaries = await CreateHandler().HandleAsync(new ListFieldTestSessionsQuery(CategoryId));

        Assert.Equal(2, summaries.Count);
        Assert.Equal(sessions[0].Id, summaries[0].Id);
        Assert.Equal(CategoryId, summaries[0].CategoryId);
        Assert.Equal("Planned", summaries[0].Status);
        Assert.Equal("Closed", summaries[1].Status);

        _fieldTests.Verify(
            repo => repo.ListSessionsByCategoryAsync(CategoryId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithoutSessions_ReturnsEmptyList()
    {
        _fieldTests
            .Setup(repo => repo.ListSessionsByCategoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var summaries = await CreateHandler().HandleAsync(new ListFieldTestSessionsQuery(CategoryId));

        Assert.Empty(summaries);
    }
}
