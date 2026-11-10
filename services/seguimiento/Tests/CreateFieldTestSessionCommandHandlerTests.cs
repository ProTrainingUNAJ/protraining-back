using Application.DTOs.FieldTests;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.FieldTests.CreateFieldTestSession;
using Domain.Entities;
using Domain.Exceptions;
using Moq;

namespace Seguimiento.Tests;

// El alta valida la categoría contra Estructura (ajena o inexistente → 404) y
// las pruebas del catálogo (inexistente, vacía o repetida → 400), y persiste la sesión
// con sus pruebas en un único SaveChanges con estado inicial Planned.
public class CreateFieldTestSessionCommandHandlerTests
{
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid FirstTestId = Guid.NewGuid();
    private static readonly Guid SecondTestId = Guid.NewGuid();

    private readonly Mock<IFieldTestRepository> _fieldTests = new();
    private readonly Mock<ICatalogTestRepository> _catalog = new();
    private readonly Mock<IStructureClient> _structure = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    public CreateFieldTestSessionCommandHandlerTests()
    {
        // Por defecto la categoría es del PF y las pruebas existen; los tests puntualizan.
        _structure
            .Setup(client => client.IsOwnCategoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _catalog
            .Setup(repo => repo.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogTest("Sentadilla", MotorCapacity.Strength, "kg"));

        _unitOfWork
            .Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    private CreateFieldTestSessionCommandHandler CreateHandler() =>
        new(_fieldTests.Object, _catalog.Object, _structure.Object, _unitOfWork.Object);

    private static CreateFieldTestSessionCommand CommandValido() =>
        new(
            CategoryId,
            new DateTime(2026, 11, 9),
            "Evaluación de pretemporada",
            [
                new FieldTestSessionTestInput { CatalogTestId = FirstTestId, Order = 1 },
                new FieldTestSessionTestInput { CatalogTestId = SecondTestId, Order = 2 }
            ]);

    [Fact]
    public async Task HandleAsync_WithValidCommand_PersistsSessionWithTestsAsPlannedOnce()
    {
        FieldTestSession? savedSession = null;
        IReadOnlyList<FieldTestSessionTest>? savedTests = null;
        _fieldTests
            .Setup(repo => repo.AddSessionAsync(
                It.IsAny<FieldTestSession>(),
                It.IsAny<IReadOnlyList<FieldTestSessionTest>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FieldTestSession, IReadOnlyList<FieldTestSessionTest>, CancellationToken>(
                (session, tests, _) =>
                {
                    savedSession = session;
                    savedTests = tests;
                })
            .Returns(Task.CompletedTask);

        var dto = await CreateHandler().HandleAsync(CommandValido());

        Assert.NotNull(savedSession);
        Assert.Equal(CategoryId, savedSession!.CategoryId);
        Assert.Equal(FieldTestSessionStatus.Planned, savedSession.Status);
        Assert.Equal("Evaluación de pretemporada", savedSession.Name);

        Assert.NotNull(savedTests);
        Assert.Equal(2, savedTests!.Count);
        Assert.All(savedTests, test => Assert.Equal(savedSession.Id, test.SessionId));
        Assert.Equal([FirstTestId, SecondTestId], savedTests.Select(test => test.CatalogTestId));
        Assert.Equal([1, 2], savedTests.Select(test => test.Order));

        // La respuesta al cliente refleja la sesión encolada, con sus pruebas y sin resultados.
        Assert.Equal(savedSession.Id, dto.Id);
        Assert.Equal("Planned", dto.Status);
        Assert.Equal(2, dto.Tests.Count);
        Assert.Empty(dto.Results);

        _fieldTests.Verify(
            repo => repo.AddSessionAsync(
                It.IsAny<FieldTestSession>(),
                It.IsAny<IReadOnlyList<FieldTestSessionTest>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // Categoría inexistente o de otro PF (filtro global de Estructura) → 404 sin escribir nada.
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task HandleAsync_WithCategoryOfAnotherPf_ThrowsCategoryNotFoundWithoutSaving()
    {
        _structure
            .Setup(client => client.IsOwnCategoryAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<CategoryNotFoundException>(
            () => CreateHandler().HandleAsync(CommandValido()));

        _fieldTests.Verify(
            repo => repo.AddSessionAsync(
                It.IsAny<FieldTestSession>(),
                It.IsAny<IReadOnlyList<FieldTestSessionTest>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownCatalogTest_ThrowsDomainExceptionWithoutSaving()
    {
        _catalog
            .Setup(repo => repo.GetByIdAsync(SecondTestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogTest?)null);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(CommandValido()));

        Assert.Equal("Una o más pruebas del catálogo no existen.", exception.Message);
        _fieldTests.Verify(
            repo => repo.AddSessionAsync(
                It.IsAny<FieldTestSession>(),
                It.IsAny<IReadOnlyList<FieldTestSessionTest>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithoutTests_ThrowsDomainExceptionWithoutSaving()
    {
        var command = new CreateFieldTestSessionCommand(
            CategoryId, new DateTime(2026, 11, 9), "Evaluación", []);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(command));

        Assert.Equal("La sesión debe incluir al menos una prueba del catálogo.", exception.Message);
        _fieldTests.Verify(
            repo => repo.AddSessionAsync(
                It.IsAny<FieldTestSession>(),
                It.IsAny<IReadOnlyList<FieldTestSessionTest>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithDuplicatedTest_ThrowsDomainExceptionWithoutSaving()
    {
        var command = new CreateFieldTestSessionCommand(
            CategoryId,
            new DateTime(2026, 11, 9),
            "Evaluación",
            [
                new FieldTestSessionTestInput { CatalogTestId = FirstTestId, Order = 1 },
                new FieldTestSessionTestInput { CatalogTestId = FirstTestId, Order = 2 }
            ]);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(command));

        Assert.Equal("La sesión no puede repetir la misma prueba del catálogo.", exception.Message);
        _fieldTests.Verify(
            repo => repo.AddSessionAsync(
                It.IsAny<FieldTestSession>(),
                It.IsAny<IReadOnlyList<FieldTestSessionTest>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
