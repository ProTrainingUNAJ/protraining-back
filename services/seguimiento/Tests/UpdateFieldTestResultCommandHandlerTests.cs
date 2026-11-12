using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.UseCases.FieldTests.UpdateFieldTestResult;
using Domain.Entities;
using Moq;

namespace Seguimiento.Tests;

// KAN-425: la corrección modifica valor e intento del resultado dentro de la sesión con
// un único SaveChanges; sesión o resultado ajeno → 404 sin tocar la fila.
public class UpdateFieldTestResultCommandHandlerTests
{
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly Guid ResultId = Guid.NewGuid();

    private readonly Mock<IFieldTestRepository> _fieldTests = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly FieldTestSession _session = new()
    {
        CategoryId = Guid.NewGuid(),
        Date = new DateTime(2026, 11, 4),
        Name = "Evaluación de pretemporada",
        Status = FieldTestSessionStatus.InProgress
    };

    private readonly FieldTestResult _result = new()
    {
        SessionId = SessionId,
        CatalogTestId = Guid.NewGuid(),
        AthleteId = Guid.NewGuid(),
        Value = 42.5m,
        Attempt = 1,
        RecordedAt = DateTime.UtcNow
    };

    public UpdateFieldTestResultCommandHandlerTests()
    {
        _fieldTests
            .Setup(repo => repo.GetSessionByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_session);
        _fieldTests
            .Setup(repo => repo.GetResultAsync(SessionId, ResultId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_result);
        _unitOfWork
            .Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    private UpdateFieldTestResultCommandHandler CreateHandler() =>
        new(_fieldTests.Object, _unitOfWork.Object);

    [Fact]
    public async Task HandleAsync_WithValidCommand_UpdatesValueAndAttemptOnce()
    {
        var dto = await CreateHandler().HandleAsync(
            new UpdateFieldTestResultCommand(SessionId, ResultId, 41.8m, 2));

        Assert.Equal(41.8m, _result.Value);
        Assert.Equal(2, _result.Attempt);
        // El resto de la fila queda intacta.
        Assert.Equal(SessionId, _result.SessionId);

        Assert.Equal(_result.Id, dto.Id);
        Assert.Equal(41.8m, dto.Value);
        Assert.Equal(2, dto.Attempt);

        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownSession_ThrowsSessionNotFoundWithoutSaving()
    {
        _fieldTests
            .Setup(repo => repo.GetSessionByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FieldTestSession?)null);

        await Assert.ThrowsAsync<FieldTestSessionNotFoundException>(
            () => CreateHandler().HandleAsync(
                new UpdateFieldTestResultCommand(SessionId, ResultId, 41.8m, 2)));

        _fieldTests.Verify(
            repo => repo.GetResultAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // KAN-425: resultado inexistente, de otra sesión o de otro PF → 404 y la fila queda intacta.
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task HandleAsync_WithResultOfAnotherPf_ThrowsResultNotFoundWithoutSaving()
    {
        _fieldTests
            .Setup(repo => repo.GetResultAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FieldTestResult?)null);

        await Assert.ThrowsAsync<FieldTestResultNotFoundException>(
            () => CreateHandler().HandleAsync(
                new UpdateFieldTestResultCommand(SessionId, ResultId, 41.8m, 2)));

        Assert.Equal(42.5m, _result.Value);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // La sesión cerrada ya no admite correcciones → 400 y la fila queda intacta.
    [Fact]
    public async Task HandleAsync_WithClosedSession_ThrowsDomainExceptionWithoutSaving()
    {
        _session.Status = FieldTestSessionStatus.Closed;

        var exception = await Assert.ThrowsAsync<Domain.Exceptions.DomainException>(
            () => CreateHandler().HandleAsync(
                new UpdateFieldTestResultCommand(SessionId, ResultId, 41.8m, 2)));

        Assert.Equal("La sesión ya está cerrada.", exception.Message);
        Assert.Equal(42.5m, _result.Value);
        _fieldTests.Verify(
            repo => repo.GetResultAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // KAN-568 CA3: corregir con PUT limpia ConfirmedAt para que la marca
    // corregida se pueda reconfirmar.
    [Fact]
    public async Task HandleAsync_WithConfirmedResult_ClearsConfirmedAt()
    {
        _result.ConfirmedAt = DateTime.UtcNow.AddMinutes(-5);

        var dto = await CreateHandler().HandleAsync(
            new UpdateFieldTestResultCommand(SessionId, ResultId, 41.8m, 1));

        Assert.Null(_result.ConfirmedAt);
        Assert.Equal(41.8m, dto.Value);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
