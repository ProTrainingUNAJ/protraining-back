using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.UseCases.FieldTests.UpdateFieldTestSessionStatus;
using Domain.Entities;
using Domain.Exceptions;
using Moq;

namespace Seguimiento.Tests;

// La máquina de estados es estricta y secuencial (Planned → InProgress → Closed),
// sin saltos ni reaperturas; sesión ajena o inexistente → 404 sin escribir nada.
public class UpdateFieldTestSessionStatusCommandHandlerTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    private readonly Mock<IFieldTestRepository> _fieldTests = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly FieldTestSession _session = new()
    {
        CategoryId = Guid.NewGuid(),
        Date = new DateTime(2026, 11, 9),
        Name = "Evaluación de pretemporada",
        Status = FieldTestSessionStatus.Planned
    };

    public UpdateFieldTestSessionStatusCommandHandlerTests()
    {
        _fieldTests
            .Setup(repo => repo.GetSessionByIdForUpdateAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_session);

        _unitOfWork
            .Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    private UpdateFieldTestSessionStatusCommandHandler CreateHandler() =>
        new(_fieldTests.Object, _unitOfWork.Object);

    [Fact]
    public async Task HandleAsync_FromPlannedToInProgress_UpdatesStatusOnce()
    {
        var dto = await CreateHandler().HandleAsync(
            new UpdateFieldTestSessionStatusCommand(SessionId, FieldTestSessionStatus.InProgress));

        Assert.Equal(FieldTestSessionStatus.InProgress, _session.Status);
        Assert.Equal(_session.Id, dto.Id);
        Assert.Equal("InProgress", dto.Status);

        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_FromInProgressToClosed_UpdatesStatusOnce()
    {
        _session.Status = FieldTestSessionStatus.InProgress;

        var dto = await CreateHandler().HandleAsync(
            new UpdateFieldTestSessionStatusCommand(SessionId, FieldTestSessionStatus.Closed));

        Assert.Equal(FieldTestSessionStatus.Closed, _session.Status);
        Assert.Equal("Closed", dto.Status);

        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // Sin salto directo de Planned a Closed.
    [Fact]
    public async Task HandleAsync_FromPlannedToClosed_ThrowsDomainExceptionWithoutSaving()
    {
        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(
                new UpdateFieldTestSessionStatusCommand(SessionId, FieldTestSessionStatus.Closed)));

        Assert.Equal(
            $"Transición de estado no válida: {FieldTestSessionStatus.Planned} → {FieldTestSessionStatus.Closed}.",
            exception.Message);
        Assert.Equal(FieldTestSessionStatus.Planned, _session.Status);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Sin reaperturas desde Closed.
    [Fact]
    public async Task HandleAsync_FromClosedToInProgress_ThrowsDomainExceptionWithoutSaving()
    {
        _session.Status = FieldTestSessionStatus.Closed;

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(
                new UpdateFieldTestSessionStatusCommand(SessionId, FieldTestSessionStatus.InProgress)));

        Assert.Equal(
            $"Transición de estado no válida: {FieldTestSessionStatus.Closed} → {FieldTestSessionStatus.InProgress}.",
            exception.Message);
        Assert.Equal(FieldTestSessionStatus.Closed, _session.Status);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Repetir el estado actual también es una transición inválida.
    [Fact]
    public async Task HandleAsync_ToSameStatus_ThrowsDomainExceptionWithoutSaving()
    {
        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(
                new UpdateFieldTestSessionStatusCommand(SessionId, FieldTestSessionStatus.Planned)));

        Assert.Equal(
            $"Transición de estado no válida: {FieldTestSessionStatus.Planned} → {FieldTestSessionStatus.Planned}.",
            exception.Message);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Sesión inexistente o de otro PF (filtro global) → 404 sin escribir nada.
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task HandleAsync_WithSessionOfAnotherPf_ThrowsSessionNotFoundWithoutSaving()
    {
        _fieldTests
            .Setup(repo => repo.GetSessionByIdForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FieldTestSession?)null);

        await Assert.ThrowsAsync<FieldTestSessionNotFoundException>(
            () => CreateHandler().HandleAsync(
                new UpdateFieldTestSessionStatusCommand(SessionId, FieldTestSessionStatus.InProgress)));

        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
