using Application.Interfaces.Persistence;
using Application.UseCases.CheckIns.SubmitCheckIn;
using Domain.Common;
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
    private readonly Mock<ICheckInInjuryPainRepository> _injuryPains = new();
    private readonly Mock<IAthleteInjuryRepository> _injuries = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly TrafficLightEngine _engine = new();

    public SubmitCheckInCommandHandlerTests()
    {
        // KAN-560: el handler consulta la ventana de 7 días de sueño en cada envío.
        // Por defecto, sin historial (los tests que lo necesiten lo re-configuran).
        _checkIns
            .Setup(repo => repo.GetByAthleteAndDateRangeAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CheckIn>());
    _injuries
        .Setup(repo => repo.ListNotClearedByAthleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(Array.Empty<AthleteInjury>());
        _unitOfWork
            .Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    private SubmitCheckInCommandHandler CreateHandler() =>
        new(_checkIns.Object, _injuryPains.Object, _injuries.Object, _unitOfWork.Object, _engine);

    private static SubmitCheckInCommand CommandValido(bool hasActiveInjury = false, double sleepHours = 8) =>
        new(AthleteId, sleepHours, 4, 1, 2, 4, 2, false, 4, null, hasActiveInjury);

    private void ConfigurarVentanaDeSueno(params CheckIn[] historial)
    {
        _checkIns
            .Setup(repo => repo.GetByAthleteAndDateRangeAsync(
                AthleteId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(historial);
    }

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

        var (response, _) = await CreateHandler().HandleAsync(
            CommandValido(hasActiveInjury: true));

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
            Date = ArgentinaTime.Today.ToDateTime(TimeOnly.MinValue),
            TrafficLight = TrafficLight.Yellow
        };
        _checkIns
            .Setup(repo => repo.GetByAthleteAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existente);
        _unitOfWork
            .Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var (response, _) = await CreateHandler().HandleAsync(
            CommandValido());

        Assert.Equal("Green", response.TrafficLight);
        Assert.Equal("Estado óptimo", response.TrafficLightReason);
        Assert.Equal(TrafficLight.Green, existente.TrafficLight);
        Assert.Equal("Estado óptimo", existente.TrafficLightReason);

        // Upsert: el registro del día se actualiza, no se duplica.
        _checkIns.Verify(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithVeryLittleSleep_ReturnsRedWithLocalReason()
    {
        ConfigurarSinCheckInPrevio();
        _checkIns
            .Setup(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var (response, _) = await CreateHandler().HandleAsync(
            CommandValido(sleepHours: 3));

        // KAN-560: dormir menos de 4 h es señal Roja, no Amarilla.
        Assert.Equal("Red", response.TrafficLight);
        Assert.Equal("Muy poco sueño", response.TrafficLightReason);
    }

    [Fact]
    public async Task HandleAsync_WithModerateSleep_ReturnsYellowWithLocalReason()
    {
        ConfigurarSinCheckInPrevio();
        _checkIns
            .Setup(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var (response, _) = await CreateHandler().HandleAsync(
            CommandValido(sleepHours: 6));

        Assert.Equal("Yellow", response.TrafficLight);
        Assert.Equal("Sueño moderado", response.TrafficLightReason);
    }

    // ---------- Normalización de sueño (KAN-560): > 12 se guarda como 13 ----------

    [Theory]
    [InlineData(13, 13)] // "Más de 12" del front
    [InlineData(16, 13)]
    [InlineData(24, 13)] // el máximo del contrato se guarda como 13
    [InlineData(12, 12)] // hasta 12, tal cual
    [InlineData(4.5, 4.5)] // decimales dentro de rango, sin tocar
    public async Task HandleAsync_NormalizesSleepHoursAbove12_To13(double recibido, double esperado)
    {
        ConfigurarSinCheckInPrevio();
        CheckIn? guardado = null;
        _checkIns
            .Setup(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()))
            .Callback<CheckIn, CancellationToken>((checkIn, _) => guardado = checkIn)
            .Returns(Task.CompletedTask);

        await CreateHandler().HandleAsync(CommandValido(sleepHours: recibido));

        Assert.NotNull(guardado);
        Assert.Equal(esperado, guardado!.SleepHours);
    }

    // ---------- Sueño prolongado: la ventana de 7 días llega desde el repositorio (KAN-560) ----------

    [Fact]
    public async Task HandleAsync_WithProlongedSleepInThreeOfSevenDays_PersistsYellowWithFixedReason()
    {
        ConfigurarSinCheckInPrevio();
        CheckIn? guardado = null;
        _checkIns
            .Setup(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()))
            .Callback<CheckIn, CancellationToken>((checkIn, _) => guardado = checkIn)
            .Returns(Task.CompletedTask);

        var historial = Enumerable.Range(1, 3)
            .Select(i => new CheckIn
            {
                AthleteId = AthleteId,
                Date = ArgentinaTime.Today.AddDays(-i).ToDateTime(TimeOnly.MinValue),
                SleepHours = 13,
                IsSick = false
            })
            .ToArray();
        ConfigurarVentanaDeSueno(historial);

        var (response, _) = await CreateHandler().HandleAsync(CommandValido(sleepHours: 8));

        Assert.Equal("Yellow", response.TrafficLight);
        Assert.Equal(TrafficLightEngine.ProlongedSleepReason, response.TrafficLightReason);
        Assert.NotNull(guardado);
        Assert.Equal(TrafficLight.Yellow, guardado!.TrafficLight);
        Assert.Equal(TrafficLightEngine.ProlongedSleepReason, guardado.TrafficLightReason);
    }

    [Fact]
    public async Task HandleAsync_WithProlongedSleepInTwoOfSevenDays_DoesNotAlert()
    {
        ConfigurarSinCheckInPrevio();
        _checkIns
            .Setup(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var historial = Enumerable.Range(1, 2)
            .Select(i => new CheckIn
            {
                AthleteId = AthleteId,
                Date = ArgentinaTime.Today.AddDays(-i).ToDateTime(TimeOnly.MinValue),
                SleepHours = 13,
                IsSick = false
            })
            .ToArray();
        ConfigurarVentanaDeSueno(historial);

        var (response, _) = await CreateHandler().HandleAsync(CommandValido(sleepHours: 8));

        Assert.Equal("Green", response.TrafficLight);
        Assert.Equal("Estado óptimo", response.TrafficLightReason);
    }

    [Fact]
    public async Task HandleAsync_TodayCountsInWindow_ThirdProlongedDayComesFromToday()
    {
        // Hoy se evalúa con el check-in recién armado (su fila todavía no está en la base):
        // 2 días prolongados previos + las 13 h de hoy completan los 3 de 7.
        ConfigurarSinCheckInPrevio();
        _checkIns
            .Setup(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var historial = Enumerable.Range(1, 2)
            .Select(i => new CheckIn
            {
                AthleteId = AthleteId,
                Date = ArgentinaTime.Today.AddDays(-i).ToDateTime(TimeOnly.MinValue),
                SleepHours = 13,
                IsSick = false
            })
            .ToArray();
        ConfigurarVentanaDeSueno(historial);

        var (response, _) = await CreateHandler().HandleAsync(CommandValido(sleepHours: 20));

        Assert.Equal("Yellow", response.TrafficLight);
        Assert.Equal(TrafficLightEngine.ProlongedSleepReason, response.TrafficLightReason);
    }

    [Fact]
    public async Task HandleAsync_SickProlongedDays_DoNotCount()
    {
        ConfigurarSinCheckInPrevio();
        _checkIns
            .Setup(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // 2 días prolongados sanos + 1 día prolongado enfermo → no llega a 3.
        var historial = new[]
        {
            new CheckIn { AthleteId = AthleteId, Date = ArgentinaTime.Today.AddDays(-1).ToDateTime(TimeOnly.MinValue), SleepHours = 13, IsSick = false },
            new CheckIn { AthleteId = AthleteId, Date = ArgentinaTime.Today.AddDays(-2).ToDateTime(TimeOnly.MinValue), SleepHours = 13, IsSick = false },
            new CheckIn { AthleteId = AthleteId, Date = ArgentinaTime.Today.AddDays(-3).ToDateTime(TimeOnly.MinValue), SleepHours = 13, IsSick = true },
        };
        ConfigurarVentanaDeSueno(historial);

        var (response, _) = await CreateHandler().HandleAsync(CommandValido(sleepHours: 8));

        Assert.Equal("Green", response.TrafficLight);
        Assert.Equal("Estado óptimo", response.TrafficLightReason);
    }

    [Fact]
    public async Task HandleAsync_WithSleepHoursOutOfRange_ThrowsDomainExceptionWithoutSaving()
    {
        ConfigurarSinCheckInPrevio();

        await Assert.ThrowsAsync<DomainException>(() => CreateHandler().HandleAsync(
            CommandValido(sleepHours: 30)));

        _checkIns.Verify(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithInvalidAthlete_ThrowsDomainException()
    {
        ConfigurarSinCheckInPrevio();

        await Assert.ThrowsAsync<DomainException>(() => CreateHandler().HandleAsync(
            new SubmitCheckInCommand(Guid.Empty, 8, 4, 1, 2, 4, 2, false, 4, null, false)));
    }
    [Fact]
    public async Task HandleAsync_UsesArgentinaDate_InsteadOfUtcDate()
    {
        DateTime? fechaRecibida = null;
        _checkIns
            .Setup(repo => repo.GetByAthleteAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTime, CancellationToken>((_, fecha, _) => fechaRecibida = fecha)
            .ReturnsAsync((CheckIn?)null);
        _checkIns
            .Setup(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWork
            .Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await CreateHandler().HandleAsync(CommandValido());

        Assert.NotNull(fechaRecibida);
        Assert.Equal(ArgentinaTime.Today, DateOnly.FromDateTime(fechaRecibida!.Value));
    }

    [Fact]
    public async Task HandleAsync_WithNegativeSleepHours_ThrowsDomainExceptionWithoutSaving()
    {
        ConfigurarSinCheckInPrevio();

        await Assert.ThrowsAsync<DomainException>(() => CreateHandler().HandleAsync(
            CommandValido(sleepHours: -1)));

        _checkIns.Verify(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithSleepHoursAbove24_ThrowsDomainExceptionWithoutSaving()
    {
        ConfigurarSinCheckInPrevio();

        await Assert.ThrowsAsync<DomainException>(() => CreateHandler().HandleAsync(
            CommandValido(sleepHours: 24.1)));

        _checkIns.Verify(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithSleepQualityOutOfRange_ThrowsDomainExceptionWithoutSaving()
    {
        ConfigurarSinCheckInPrevio();

        await Assert.ThrowsAsync<DomainException>(() => CreateHandler().HandleAsync(
            new SubmitCheckInCommand(AthleteId, 8, 0, 1, 2, 4, 2, false, 4, null, false)));

        _checkIns.Verify(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithMuscleSorenessOutOfRange_ThrowsDomainExceptionWithoutSaving()
    {
        ConfigurarSinCheckInPrevio();

        await Assert.ThrowsAsync<DomainException>(() => CreateHandler().HandleAsync(
            new SubmitCheckInCommand(AthleteId, 8, 4, 6, 2, 4, 2, false, 4, null, false)));

        _checkIns.Verify(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithFatigueLevelOutOfRange_ThrowsDomainExceptionWithoutSaving()
    {
        ConfigurarSinCheckInPrevio();

        await Assert.ThrowsAsync<DomainException>(() => CreateHandler().HandleAsync(
            new SubmitCheckInCommand(AthleteId, 8, 4, 1, 0, 4, 2, false, 4, null, false)));

        _checkIns.Verify(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithMoodRateOutOfRange_ThrowsDomainExceptionWithoutSaving()
    {
        ConfigurarSinCheckInPrevio();

        await Assert.ThrowsAsync<DomainException>(() => CreateHandler().HandleAsync(
            new SubmitCheckInCommand(AthleteId, 8, 4, 1, 2, 6, 2, false, 4, null, false)));

        _checkIns.Verify(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithStressLevelOutOfRange_ThrowsDomainExceptionWithoutSaving()
    {
        ConfigurarSinCheckInPrevio();

        await Assert.ThrowsAsync<DomainException>(() => CreateHandler().HandleAsync(
            new SubmitCheckInCommand(AthleteId, 8, 4, 1, 2, 4, 0, false, 4, null, false)));

        _checkIns.Verify(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithEffortRateOutOfRange_ThrowsDomainExceptionWithoutSaving()
    {
        ConfigurarSinCheckInPrevio();

        await Assert.ThrowsAsync<DomainException>(() => CreateHandler().HandleAsync(
            new SubmitCheckInCommand(AthleteId, 8, 4, 1, 2, 4, 2, false, 11, null, false)));

        _checkIns.Verify(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_TrimsPainZones()
    {
        ConfigurarSinCheckInPrevio();
        CheckIn? guardado = null;
        _checkIns
            .Setup(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()))
            .Callback<CheckIn, CancellationToken>((checkIn, _) => guardado = checkIn)
            .Returns(Task.CompletedTask);

        await CreateHandler().HandleAsync(
            CommandValido() with { PainZones = "  rodilla  " });

        Assert.NotNull(guardado);
        Assert.Equal("rodilla", guardado!.PainZones);
    }

    [Fact]
    public async Task HandleAsync_WithOriginalDate_KeepsThatDateInsteadOfToday()
    {
        DateTime? fechaRecibida = null;
        CheckIn? guardado = null;
        _checkIns
            .Setup(repo => repo.GetByAthleteAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTime, CancellationToken>((_, fecha, _) => fechaRecibida = fecha)
            .ReturnsAsync((CheckIn?)null);
        _checkIns
            .Setup(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()))
            .Callback<CheckIn, CancellationToken>((checkIn, _) => guardado = checkIn)
            .Returns(Task.CompletedTask);
        _unitOfWork
            .Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var originalDate = ArgentinaTime.Today.AddDays(-1);
        await CreateHandler().HandleAsync(CommandValido() with { Date = originalDate });

        Assert.NotNull(fechaRecibida);
        Assert.Equal(originalDate, DateOnly.FromDateTime(fechaRecibida!.Value));
        Assert.NotNull(guardado);
        Assert.Equal(originalDate, DateOnly.FromDateTime(guardado!.Date));
    }

    [Fact]
    public async Task HandleAsync_WithFutureDate_ThrowsDomainExceptionWithoutSaving()
    {
        ConfigurarSinCheckInPrevio();

        await Assert.ThrowsAsync<DomainException>(() => CreateHandler().HandleAsync(
            CommandValido() with { Date = ArgentinaTime.Today.AddDays(1) }));

        _checkIns.Verify(repo => repo.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
