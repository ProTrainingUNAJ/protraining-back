using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.FieldTests.CreateFieldTestResult;
using Domain.Entities;
using Moq;

namespace Seguimiento.Tests;

// KAN-425: la carga rápida persiste el resultado con un único SaveChanges, rechaza con
// 404 la sesión o el atleta de otro PF sin escribir nada y propaga el rechazo del índice
// único (duplicado) como PersistenceConflictException → 409.
public class CreateFieldTestResultCommandHandlerTests
{
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly Guid CatalogTestId = Guid.NewGuid();
    private static readonly Guid AthleteId = Guid.NewGuid();

    private readonly Mock<IFieldTestRepository> _fieldTests = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IStructureClient> _structure = new();

    private readonly FieldTestSession _session = new()
    {
        CategoryId = Guid.NewGuid(),
        Date = new DateTime(2026, 11, 4),
        Name = "Evaluación de pretemporada",
        Status = FieldTestSessionStatus.InProgress
    };

    public CreateFieldTestResultCommandHandlerTests()
    {
        _fieldTests
            .Setup(repo => repo.GetSessionByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_session);

        // Por defecto el atleta es del PF; el test de aislamiento lo cambia.
        _structure
            .Setup(client => client.IsOwnAthleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _unitOfWork
            .Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    private CreateFieldTestResultCommandHandler CreateHandler() =>
        new(_fieldTests.Object, _structure.Object, _unitOfWork.Object);

    private static CreateFieldTestResultCommand CommandValido() =>
        new(SessionId, CatalogTestId, AthleteId, 42.5m, 1);

    [Fact]
    public async Task HandleAsync_WithValidCommand_PersistsResultOnceAndMapsDto()
    {
        FieldTestResult? guardado = null;
        _fieldTests
            .Setup(repo => repo.AddResultAsync(It.IsAny<FieldTestResult>(), It.IsAny<CancellationToken>()))
            .Callback<FieldTestResult, CancellationToken>((result, _) => guardado = result)
            .Returns(Task.CompletedTask);

        var dto = await CreateHandler().HandleAsync(CommandValido());

        Assert.NotNull(guardado);
        Assert.Equal(SessionId, guardado!.SessionId);
        Assert.Equal(CatalogTestId, guardado.CatalogTestId);
        Assert.Equal(AthleteId, guardado.AthleteId);
        Assert.Equal(42.5m, guardado.Value);
        Assert.Equal(1, guardado.Attempt);
        Assert.NotEqual(default, guardado.RecordedAt);

        // La respuesta al cliente refleja la fila encolada.
        Assert.Equal(guardado.Id, dto.Id);
        Assert.Equal(42.5m, dto.Value);
        Assert.Equal(1, dto.Attempt);

        _fieldTests.Verify(
            repo => repo.AddResultAsync(It.IsAny<FieldTestResult>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownSession_ThrowsSessionNotFoundWithoutSaving()
    {
        _fieldTests
            .Setup(repo => repo.GetSessionByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FieldTestSession?)null);

        await Assert.ThrowsAsync<FieldTestSessionNotFoundException>(
            () => CreateHandler().HandleAsync(CommandValido()));

        _fieldTests.Verify(
            repo => repo.AddResultAsync(It.IsAny<FieldTestResult>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // KAN-425: atleta de otro PF (o inexistente) → 404 y no se carga el resultado.
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task HandleAsync_WithAthleteOfAnotherPf_ThrowsAthleteNotFoundWithoutSaving()
    {
        _structure
            .Setup(client => client.IsOwnAthleteAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<AthleteNotFoundException>(
            () => CreateHandler().HandleAsync(CommandValido()));

        _fieldTests.Verify(
            repo => repo.AddResultAsync(It.IsAny<FieldTestResult>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // El duplicado lo detecta la base en el SaveChanges (índice único) y se propaga como 409.
    [Fact]
    public async Task HandleAsync_WhenDatabaseRejectsDuplicate_PropagatesConflict()
    {
        _fieldTests
            .Setup(repo => repo.AddResultAsync(It.IsAny<FieldTestResult>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWork
            .Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PersistenceConflictException());

        await Assert.ThrowsAsync<PersistenceConflictException>(
            () => CreateHandler().HandleAsync(CommandValido()));

        _fieldTests.Verify(
            repo => repo.AddResultAsync(It.IsAny<FieldTestResult>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // La sesión cerrada ya no admite carga de marcas → 400 sin tocar nada.
    [Fact]
    public async Task HandleAsync_WithClosedSession_ThrowsDomainExceptionWithoutSaving()
    {
        _session.Status = FieldTestSessionStatus.Closed;

        var exception = await Assert.ThrowsAsync<Domain.Exceptions.DomainException>(
            () => CreateHandler().HandleAsync(CommandValido()));

        Assert.Equal("La sesión ya está cerrada.", exception.Message);
        _structure.Verify(
            client => client.IsOwnAthleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _fieldTests.Verify(
            repo => repo.AddResultAsync(It.IsAny<FieldTestResult>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
