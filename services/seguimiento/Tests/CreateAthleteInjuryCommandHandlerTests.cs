using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Injuries.CreateAthleteInjury;
using Domain.Entities;
using Domain.Exceptions;
using Moq;

namespace Seguimiento.Tests;

// KAN-419: el alta persiste la lesión en Active con su fecha de retorno estimada y,
// si el dominio la rechaza, no escribe nada (ni la lesión ni el historial). Además
// deja la fila inicial null → Active del historial clínico.
public class CreateAthleteInjuryCommandHandlerTests
{
    private static readonly Guid AthleteId = Guid.NewGuid();
    private static readonly Guid PfId = Guid.NewGuid();
    private static readonly DateTime InjuryDate = new(2026, 10, 20);
    private static readonly DateTime EstimatedReturnDate = new(2026, 11, 10);

    private readonly Mock<IAthleteInjuryRepository> _injuries = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IStructureClient> _structure = new();

    public CreateAthleteInjuryCommandHandlerTests()
    {
        _currentUser.Setup(user => user.PfId).Returns(PfId);

        // Por defecto el atleta es del PF; el test de KAN-431 lo cambia.
        _structure
            .Setup(client => client.IsOwnAthleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private CreateAthleteInjuryCommandHandler CreateHandler() =>
        new(_injuries.Object, _unitOfWork.Object, _currentUser.Object, _structure.Object);

    private static CreateAthleteInjuryCommand CommandValido(DateTime? estimatedReturnDate) =>
        new(AthleteId, "isquiotibiales", "Gradación 3 tras sprint", InjuryDate,
            estimatedReturnDate);

    [Fact]
    public async Task HandleAsync_WithValidCommand_PersistsInjuryActiveAndItsInitialHistoryRow()
    {
        AthleteInjury? guardada = null;
        _injuries
            .Setup(repo => repo.AddAsync(It.IsAny<AthleteInjury>(), It.IsAny<CancellationToken>()))
            .Callback<AthleteInjury, CancellationToken>((injury, _) => guardada = injury)
            .Returns(Task.CompletedTask);
        AthleteInjuryStatusChange? filaInicial = null;
        _injuries
            .Setup(repo => repo.AddStatusChangeAsync(It.IsAny<AthleteInjuryStatusChange>(), It.IsAny<CancellationToken>()))
            .Callback<AthleteInjuryStatusChange, CancellationToken>((row, _) => filaInicial = row)
            .Returns(Task.CompletedTask);
        _unitOfWork
            .Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var dto = await CreateHandler().HandleAsync(CommandValido(EstimatedReturnDate));

        Assert.NotNull(guardada);
        Assert.Equal(InjuryStatus.Active, guardada!.Status);
        Assert.Equal(AthleteId, guardada.AthleteId);
        Assert.Equal(EstimatedReturnDate, guardada.EstimatedReturnDate);

        // La respuesta al cliente también arranca en Active.
        Assert.Equal("Active", dto.Status);
        Assert.Equal(guardada.Id, dto.Id);

        // Fila inicial del historial: null → Active, atada a la lesión y al PF del token.
        Assert.NotNull(filaInicial);
        Assert.Equal(guardada.Id, filaInicial!.InjuryId);
        Assert.Null(filaInicial.FromStatus);
        Assert.Equal(InjuryStatus.Active, filaInicial.ToStatus);
        Assert.Equal(PfId, filaInicial.ChangedBy);
        Assert.NotEqual(default, filaInicial.ChangedAt);

        _injuries.Verify(
            repo => repo.AddStatusChangeAsync(It.IsAny<AthleteInjuryStatusChange>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithoutPfInToken_ThrowsUnauthorizedWithoutSaving()
    {
        _currentUser.Setup(user => user.PfId).Returns((Guid?)null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => CreateHandler().HandleAsync(CommandValido(EstimatedReturnDate)));

        _injuries.Verify(repo => repo.AddAsync(It.IsAny<AthleteInjury>(), It.IsAny<CancellationToken>()), Times.Never);
        _injuries.Verify(
            repo => repo.AddStatusChangeAsync(It.IsAny<AthleteInjuryStatusChange>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithoutEstimatedReturnDate_ThrowsDomainExceptionWithoutSaving()
    {
        await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(CommandValido(estimatedReturnDate: null)));

        _injuries.Verify(repo => repo.AddAsync(It.IsAny<AthleteInjury>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithEstimatedReturnDateBeforeInjuryDate_ThrowsDomainExceptionWithoutSaving()
    {
        await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(CommandValido(InjuryDate.AddDays(-1))));

        _injuries.Verify(repo => repo.AddAsync(It.IsAny<AthleteInjury>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithEmptyZone_ThrowsDomainExceptionWithoutSaving()
    {
        await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(CommandValido(EstimatedReturnDate) with { Zone = " " }));

        _injuries.Verify(repo => repo.AddAsync(It.IsAny<AthleteInjury>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownZone_ThrowsDomainExceptionWithoutSaving()
    {
        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(CommandValido(EstimatedReturnDate) with { Zone = "femoral" }));

        Assert.Equal(
            "La zona de la lesión no es válida: debe ser una de las 19 zonas del check-in (ej.: isquiotibiales).",
            exception.Message);

        _injuries.Verify(repo => repo.AddAsync(It.IsAny<AthleteInjury>(), It.IsAny<CancellationToken>()), Times.Never);
        _injuries.Verify(
            repo => repo.AddStatusChangeAsync(It.IsAny<AthleteInjuryStatusChange>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // KAN-431: atleta de otro PF (o inexistente) → 404 y no se guarda nada.
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task HandleAsync_WithAthleteOfAnotherPf_ThrowsAthleteNotFoundWithoutSaving()
    {
        _structure
            .Setup(client => client.IsOwnAthleteAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<AthleteNotFoundException>(
            () => CreateHandler().HandleAsync(CommandValido(EstimatedReturnDate)));

        _injuries.Verify(repo => repo.AddAsync(It.IsAny<AthleteInjury>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
