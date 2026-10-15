using Application.DTOs.CheckIns;
using Application.Interfaces.Persistence;
using Application.UseCases.CheckIns.SubmitCheckIn;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Services;
using Moq;

namespace Seguimiento.Tests;

// Demo 1 (KAN-363/KAN-364): el check-in real entra por el caso de uso, el motor local decide el color
// y el resultado se persiste en la tabla de check-ins. Ni el handler ni el motor hablan por HTTP.
public class SubmitCheckInCommandHandlerTests
{
    private static readonly Guid AthleteId = Guid.NewGuid();

    private readonly Mock<ICheckInRepository> _checkIns = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly TrafficLightEngine _engine = new();

    private SubmitCheckInCommandHandler CreateHandler() =>
        new(_checkIns.Object, _unitOfWork.Object, _engine);

    private static CheckInRequestDto RequestValido(bool hasActiveInjury = false, double sleepHours = 8) => new()
    {
        SleepHours = sleepHours,
        SleepQuality = 4,
        MuscleSoreness = 1,
        FatigueLevel = 2,
        MoodRate = 4,
        StressLevel = 2,
        EffortRate = 4,
        HasActiveInjury = hasActiveInjury
    };

    private void ConfigurarSinCheckInPrevio()
    {
        _checkIns
            .Setup(repo => repo.GetByAthleteAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CheckIn?)null);
        _unitOfWork
            .Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    [Fact]
    public async Task HandleAsync_WithActiveInjury_ReturnsRedWithActiveInjuryReason()
    {
        ConfigurarSinCheckInPrevio();
        CheckIn? guardado = null;
        _checkIns
            .Setup(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()))
            .Callback<CheckIn, CancellationToken>((checkIn, _) => guardado = checkIn)
            .Returns(Task.CompletedTask);

        var response = await CreateHandler().HandleAsync(
            new SubmitCheckInCommand(AthleteId, RequestValido(hasActiveInjury: true)));

        // Respuesta de la Demo 1: Rojo con el motivo literal.
        Assert.Equal("Red", response.TrafficLight);
        Assert.Equal("Lesión activa", response.TrafficLightReason);

        // El color y el motivo quedan persistidos en la tabla local de check-ins.
        Assert.NotNull(guardado);
        Assert.Equal(TrafficLight.Red, guardado.TrafficLight);
        Assert.Equal("Lesión activa", guardado.TrafficLightReason);
        Assert.True(guardado.HasActiveInjury);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithoutInjuryOnGreenPath_UpdatesTodaysCheckIn()
    {
        var existente = new CheckIn
        {
            AthleteId = AthleteId,
            Date = DateTime.UtcNow.Date,
            TrafficLight = TrafficLight.Yellow
        };
        _checkIns
            .Setup(repo => repo.GetByAthleteAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existente);
        _unitOfWork
            .Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var response = await CreateHandler().HandleAsync(
            new SubmitCheckInCommand(AthleteId, RequestValido()));

        Assert.Equal("Green", response.TrafficLight);
        Assert.Equal("Estado óptimo", response.TrafficLightReason);
        Assert.Equal(TrafficLight.Green, existente.TrafficLight);
        Assert.Equal("Estado óptimo", existente.TrafficLightReason);

        // Upsert: el registro del día se actualiza, no se duplica.
        _checkIns.Verify(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithLittleSleep_ReturnsYellowWithLocalReason()
    {
        ConfigurarSinCheckInPrevio();
        _checkIns
            .Setup(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var response = await CreateHandler().HandleAsync(
            new SubmitCheckInCommand(AthleteId, RequestValido(sleepHours: 3)));

        Assert.Equal("Yellow", response.TrafficLight);
        Assert.Equal("Señales de alerta: poco sueño", response.TrafficLightReason);
    }

    [Fact]
    public async Task HandleAsync_WithSleepHoursOutOfRange_ThrowsDomainExceptionWithoutSaving()
    {
        ConfigurarSinCheckInPrevio();

        await Assert.ThrowsAsync<DomainException>(() => CreateHandler().HandleAsync(
            new SubmitCheckInCommand(AthleteId, RequestValido(sleepHours: 30))));

        _checkIns.Verify(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithInvalidAthlete_ThrowsDomainException()
    {
        ConfigurarSinCheckInPrevio();

        await Assert.ThrowsAsync<DomainException>(() => CreateHandler().HandleAsync(
            new SubmitCheckInCommand(Guid.Empty, RequestValido())));
    }
}
