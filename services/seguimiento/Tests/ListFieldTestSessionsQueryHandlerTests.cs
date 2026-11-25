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
                Name = "Evaluación de pretemporada"
                // Planned por defecto (setter privado, KAN-580).
            },
            Cerrada(CategoryId, new DateTime(2026, 10, 1), "Test de arranque")
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

    // KAN-580: Status tiene setter privado, así que la sesión cerrada se alcanza
    // recorriendo las transiciones permitidas.
    private static FieldTestSession Cerrada(Guid categoryId, DateTime date, string name)
    {
        var session = new FieldTestSession
        {
            CategoryId = categoryId,
            Date = date,
            Name = name
        };

        session.TransitionTo(FieldTestSessionStatus.InProgress);
        session.TransitionTo(FieldTestSessionStatus.Closed);
        return session;
    }
}
