using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Injuries.UpdateInjuryStatus;
using Domain.Entities;
using Domain.Exceptions;
using Moq;

namespace Seguimiento.Tests;

// KAN-420: el handler carga la lesión, deja que el dominio decida la transición y persiste
// la lesión modificada junto con su fila de historial clínico en un único SaveChanges.
public class UpdateInjuryStatusCommandHandlerTests
{
    private static readonly Guid InjuryId = Guid.NewGuid();
    private static readonly Guid PfId = Guid.NewGuid();

    private readonly Mock<IAthleteInjuryRepository> _injuries = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    public UpdateInjuryStatusCommandHandlerTests()
    {
        _currentUser.Setup(user => user.PfId).Returns(PfId);
        _unitOfWork
            .Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    private UpdateInjuryStatusCommandHandler CreateHandler() =>
        new(_injuries.Object, _currentUser.Object, _unitOfWork.Object);

    private static AthleteInjury Activa() =>
        AthleteInjury.Create(
            Guid.NewGuid(), "isquiotibiales", "Gradación 3 tras sprint",
            new DateTime(2026, 10, 20), new DateTime(2026, 11, 10));

    private void Devolver(AthleteInjury? injury) =>
        _injuries
            .Setup(repo => repo.GetByIdAsync(InjuryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(injury);

    [Fact]
    public async Task HandleAsync_FromActiveToInRecovery_PersistsStatusAndHistoryRow()
    {
        var injury = Activa();
        Devolver(injury);
        AthleteInjuryStatusChange? guardada = null;
        _injuries
            .Setup(repo => repo.AddStatusChangeAsync(It.IsAny<AthleteInjuryStatusChange>(), It.IsAny<CancellationToken>()))
            .Callback<AthleteInjuryStatusChange, CancellationToken>((row, _) => guardada = row)
            .Returns(Task.CompletedTask);

        var dto = await CreateHandler().HandleAsync(
            new UpdateInjuryStatusCommand(InjuryId, InjuryStatus.InRecovery, "Inicio de readaptación."));

        Assert.Equal("InRecovery", dto.Status);
        Assert.Null(dto.ClearedAt);

        Assert.NotNull(guardada);
        // La fila se ata al id real de la entidad cargada (injury.Id).
        Assert.Equal(injury.Id, guardada!.InjuryId);
        Assert.Equal(InjuryStatus.Active, guardada.FromStatus);
        Assert.Equal(InjuryStatus.InRecovery, guardada.ToStatus);
        Assert.Equal(PfId, guardada.ChangedBy);

        _injuries.Verify(repo => repo.AddStatusChangeAsync(It.IsAny<AthleteInjuryStatusChange>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_FromInRecoveryToMedicallyCleared_LiberatesAthlete()
    {
        var injury = Activa();
        injury.TransitionTo(InjuryStatus.InRecovery, "Inicio de readaptación.", PfId, DateTime.UtcNow);
        Devolver(injury);

        var dto = await CreateHandler().HandleAsync(
            new UpdateInjuryStatusCommand(InjuryId, InjuryStatus.MedicallyCleared, "Alta confirmada."));

        Assert.Equal("MedicallyCleared", dto.Status);
        Assert.NotNull(dto.ClearedAt);
        Assert.Equal(InjuryStatus.MedicallyCleared, injury.Status);
        Assert.NotNull(injury.ClearedAt);

        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // CA1: el salto directo de Activa a Alta médica lo rechaza el dominio, sin persistir nada.
    [Fact]
    public async Task HandleAsync_DirectlyToMedicallyCleared_ThrowsDomainExceptionWithoutSaving()
    {
        Devolver(Activa());

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(
                new UpdateInjuryStatusCommand(InjuryId, InjuryStatus.MedicallyCleared, "Alta directa.")));

        Assert.Equal(
            "Una lesión activa no puede pasar directamente a 'Alta médica'. Debe pasar primero por 'En recuperación'.",
            exception.Message);

        _injuries.Verify(repo => repo.AddStatusChangeAsync(It.IsAny<AthleteInjuryStatusChange>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenInjuryDoesNotExist_ThrowsInjuryNotFound()
    {
        Devolver(null);

        await Assert.ThrowsAsync<InjuryNotFoundException>(
            () => CreateHandler().HandleAsync(
                new UpdateInjuryStatusCommand(InjuryId, InjuryStatus.InRecovery, "Nota clínica.")));

        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithoutPfInToken_ThrowsUnauthorizedWithoutSaving()
    {
        Devolver(Activa());
        _currentUser.Setup(user => user.PfId).Returns((Guid?)null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => CreateHandler().HandleAsync(
                new UpdateInjuryStatusCommand(InjuryId, InjuryStatus.InRecovery, "Nota clínica.")));

        _injuries.Verify(repo => repo.AddStatusChangeAsync(It.IsAny<AthleteInjuryStatusChange>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
