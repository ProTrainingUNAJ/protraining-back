using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Injuries.CreateAthleteInjury;
using Domain.Entities;
using Domain.Exceptions;
using Moq;

namespace Seguimiento.Tests;

// KAN-419: el alta persiste la lesión en Active con su fecha de retorno estimada y,
// si el dominio la rechaza, no escribe nada (ni la lesión ni el historial).
public class CreateAthleteInjuryCommandHandlerTests
{
    private static readonly Guid AthleteId = Guid.NewGuid();
    private static readonly DateTime InjuryDate = new(2026, 10, 20);
    private static readonly DateTime EstimatedReturnDate = new(2026, 11, 10);

    private readonly Mock<IAthleteInjuryRepository> _injuries = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IStructureClient> _structure = new();

    public CreateAthleteInjuryCommandHandlerTests()
    {
        // Por defecto el atleta es del PF; el test de KAN-431 lo cambia.
        _structure
            .Setup(client => client.IsOwnAthleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private CreateAthleteInjuryCommandHandler CreateHandler() =>
        new(_injuries.Object, _unitOfWork.Object, _structure.Object);

    private static CreateAthleteInjuryCommand CommandValido(DateTime? estimatedReturnDate) =>
        new(AthleteId, "isquiotibiales", "Gradación 3 tras sprint", InjuryDate,
            estimatedReturnDate);

    [Fact]
    public async Task HandleAsync_WithValidCommand_PersistsInjuryActiveAndSavesOnce()
    {
        AthleteInjury? guardada = null;
        _injuries
            .Setup(repo => repo.AddAsync(It.IsAny<AthleteInjury>(), It.IsAny<CancellationToken>()))
            .Callback<AthleteInjury, CancellationToken>((injury, _) => guardada = injury)
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

        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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

    // KAN-431: atleta de otro PF (o inexistente) → 404 y no se guarda nada.
    [Fact]
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
