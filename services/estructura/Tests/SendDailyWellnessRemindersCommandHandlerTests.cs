using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Reminders.SendDailyWellnessReminders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using global::Infrastructure.Data;
using global::Infrastructure.Services;
using Moq;
using Xunit;

namespace Estructura.Tests;

public class SendDailyWellnessRemindersCommandHandlerTests
{
    private readonly Mock<IAthleteRepository> _mockAthleteRepository;
    private readonly Mock<ICurrentUser> _mockCurrentUser;
    private readonly Mock<ISessionScheduleService> _mockSessionScheduleService;
    private readonly Mock<IWellnessCheckInService> _mockWellnessCheckInService;
    private readonly Mock<IWellnessReminderTracker> _mockReminderTracker;
    private readonly Mock<IPushNotificationService> _mockPushNotificationService;
    private readonly SendDailyWellnessRemindersCommandHandler _handler;
    private readonly Guid _defaultPfId = Guid.NewGuid();

    public SendDailyWellnessRemindersCommandHandlerTests()
    {
        _mockAthleteRepository = new Mock<IAthleteRepository>();
        _mockCurrentUser = new Mock<ICurrentUser>();
        _mockSessionScheduleService = new Mock<ISessionScheduleService>();
        _mockWellnessCheckInService = new Mock<IWellnessCheckInService>();
        _mockReminderTracker = new Mock<IWellnessReminderTracker>();
        _mockPushNotificationService = new Mock<IPushNotificationService>();

        _mockAthleteRepository.Setup(r => r.GetDistinctPfIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { _defaultPfId });

        _mockCurrentUser.Setup(c => c.UsePfContext(It.IsAny<Guid>()))
            .Returns(EmptyDisposable.Instance);

        _handler = new SendDailyWellnessRemindersCommandHandler(
            _mockAthleteRepository.Object,
            _mockCurrentUser.Object,
            _mockSessionScheduleService.Object,
            _mockWellnessCheckInService.Object,
            _mockReminderTracker.Object,
            _mockPushNotificationService.Object
        );
    }

    [Fact]
    public async Task Given_AthleteWithSessionAndNoCheckIn_When_Handle_Then_SendsReminderAndMarksAsSent()
    {
        // arrange: atleta con sesion hoy, sin check-in y sin recordatorio previo
        var athleteId = Guid.NewGuid();
        var today = new DateOnly(2026, 10, 16);

        _mockSessionScheduleService.Setup(s => s.GetAthletesWithScheduledSessionAsync(today, _defaultPfId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { athleteId });

        _mockWellnessCheckInService.Setup(s => s.GetCompletedCheckInAthleteIdsAsync(
                It.IsAny<IReadOnlyList<Guid>>(), today, _defaultPfId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());

        _mockReminderTracker.Setup(s => s.HasReminderBeenSentAsync(athleteId, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // act
        var result = await _handler.HandleAsync(new SendDailyWellnessRemindersCommand(today));

        // assert: recibe el recordatorio con el mensaje exacto del ticket
        Assert.Equal(1, result.RemindersSent);
        Assert.Equal(0, result.SkippedAlreadyCheckedIn);
        Assert.Equal(0, result.SkippedAlreadySent);

        _mockPushNotificationService.Verify(p => p.SendNotificationAsync(
            athleteId,
            SendDailyWellnessRemindersCommandHandler.ReminderTitle,
            SendDailyWellnessRemindersCommandHandler.ReminderMessage,
            It.IsAny<CancellationToken>()), Times.Once);

        _mockReminderTracker.Verify(t => t.MarkReminderAsSentAsync(athleteId, today, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_AthleteWithSessionAndCompletedCheckIn_When_Handle_Then_DoesNotSendReminder()
    {
        // arrange: atleta con sesion hoy que ya cargo su check-in
        var athleteId = Guid.NewGuid();
        var today = new DateOnly(2026, 10, 16);

        _mockSessionScheduleService.Setup(s => s.GetAthletesWithScheduledSessionAsync(today, _defaultPfId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { athleteId });

        _mockWellnessCheckInService.Setup(s => s.GetCompletedCheckInAthleteIdsAsync(
                It.IsAny<IReadOnlyList<Guid>>(), today, _defaultPfId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { athleteId });

        // act
        var result = await _handler.HandleAsync(new SendDailyWellnessRemindersCommand(today));

        // assert: no debe recibir nada
        Assert.Equal(0, result.RemindersSent);
        Assert.Equal(1, result.SkippedAlreadyCheckedIn);

        _mockPushNotificationService.Verify(p => p.SendNotificationAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_AthleteAlreadyRemindedToday_When_Handle_Then_DoesNotSendDuplicateReminder()
    {
        // arrange: atleta sin check-in que ya recibio un recordatorio hoy
        var athleteId = Guid.NewGuid();
        var today = new DateOnly(2026, 10, 16);

        _mockSessionScheduleService.Setup(s => s.GetAthletesWithScheduledSessionAsync(today, _defaultPfId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { athleteId });

        _mockWellnessCheckInService.Setup(s => s.GetCompletedCheckInAthleteIdsAsync(
                It.IsAny<IReadOnlyList<Guid>>(), today, _defaultPfId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());

        _mockReminderTracker.Setup(s => s.HasReminderBeenSentAsync(athleteId, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // act
        var result = await _handler.HandleAsync(new SendDailyWellnessRemindersCommand(today));

        // assert: respeta la regla de maximo 1 recordatorio por dia por atleta
        Assert.Equal(0, result.RemindersSent);
        Assert.Equal(1, result.SkippedAlreadySent);

        _mockPushNotificationService.Verify(p => p.SendNotificationAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_NoAthletesWithScheduledSession_When_Handle_Then_DoesNothing()
    {
        // arrange: dia sin sesiones programadas
        var today = new DateOnly(2026, 10, 16);

        _mockSessionScheduleService.Setup(s => s.GetAthletesWithScheduledSessionAsync(today, _defaultPfId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // act
        var result = await _handler.HandleAsync(new SendDailyWellnessRemindersCommand(today));

        // assert
        Assert.Equal(0, result.RemindersSent);
        _mockPushNotificationService.Verify(p => p.SendNotificationAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_MixedBatchOfAthletes_When_Handle_Then_OnlySendsToAthletesWithSessionAndNoCheckIn()
    {
        // arrange: lote mixto con atletas en todos los estados posibles
        var today = new DateOnly(2026, 10, 16);

        var athlete1NeedsReminder = Guid.NewGuid(); // con sesion, sin check-in, sin aviso previo -> DEBE RECIBIR
        var athlete2AlreadyCheckedIn = Guid.NewGuid(); // con sesion, ya hizo check-in -> NO RECIBE
        var athlete3AlreadyReminded = Guid.NewGuid(); // con sesion, sin check-in, pero ya avisado hoy -> NO RECIBE
        var athlete4NoSession = Guid.NewGuid(); // sin sesion programada -> NO RECIBE

        // solo los atletas 1, 2 y 3 tienen sesion hoy
        _mockSessionScheduleService.Setup(s => s.GetAthletesWithScheduledSessionAsync(today, _defaultPfId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { athlete1NeedsReminder, athlete2AlreadyCheckedIn, athlete3AlreadyReminded });

        // estados de check-in: atleta 2 ya completo
        _mockWellnessCheckInService.Setup(s => s.GetCompletedCheckInAthleteIdsAsync(
                It.IsAny<IReadOnlyList<Guid>>(), today, _defaultPfId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { athlete2AlreadyCheckedIn });

        // estados de recordatorios previos hoy
        _mockReminderTracker.Setup(s => s.HasReminderBeenSentAsync(athlete1NeedsReminder, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockReminderTracker.Setup(s => s.HasReminderBeenSentAsync(athlete3AlreadyReminded, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // act
        var result = await _handler.HandleAsync(new SendDailyWellnessRemindersCommand(today));

        // assert: solo se envio 1 recordatorio (al atleta 1)
        Assert.Equal(1, result.RemindersSent);
        Assert.Equal(1, result.SkippedAlreadyCheckedIn);
        Assert.Equal(1, result.SkippedAlreadySent);

        // verificamos que el atleta 1 recibio el aviso
        _mockPushNotificationService.Verify(p => p.SendNotificationAsync(
            athlete1NeedsReminder,
            SendDailyWellnessRemindersCommandHandler.ReminderTitle,
            SendDailyWellnessRemindersCommandHandler.ReminderMessage,
            It.IsAny<CancellationToken>()), Times.Once);

        // verificamos que los demas no recibieron nada
        _mockPushNotificationService.Verify(p => p.SendNotificationAsync(athlete2AlreadyCheckedIn, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockPushNotificationService.Verify(p => p.SendNotificationAsync(athlete3AlreadyReminded, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockPushNotificationService.Verify(p => p.SendNotificationAsync(athlete4NoSession, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_MultiplePfs_When_Handle_Then_ProcessesEachPfWithItsOwnContext()
    {
        // arrange: dos PFs distintos con un atleta cada uno
        var today = new DateOnly(2026, 10, 16);
        var pfA = Guid.NewGuid();
        var pfB = Guid.NewGuid();
        var athleteA = Guid.NewGuid();
        var athleteB = Guid.NewGuid();

        _mockAthleteRepository.Setup(r => r.GetDistinctPfIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { pfA, pfB });

        _mockSessionScheduleService.Setup(s => s.GetAthletesWithScheduledSessionAsync(today, pfA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { athleteA });
        _mockSessionScheduleService.Setup(s => s.GetAthletesWithScheduledSessionAsync(today, pfB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { athleteB });

        _mockWellnessCheckInService.Setup(s => s.GetCompletedCheckInAthleteIdsAsync(
                It.IsAny<IReadOnlyList<Guid>>(), today, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());

        _mockReminderTracker.Setup(s => s.HasReminderBeenSentAsync(It.IsAny<Guid>(), today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // act
        var result = await _handler.HandleAsync(new SendDailyWellnessRemindersCommand(today));

        // assert: se envian recordatorios para los atletas de ambos PFs
        Assert.Equal(2, result.RemindersSent);

        // verifica que se haya activado el contexto ambiental de cada PF
        _mockCurrentUser.Verify(c => c.UsePfContext(pfA), Times.Once);
        _mockCurrentUser.Verify(c => c.UsePfContext(pfB), Times.Once);

        _mockPushNotificationService.Verify(p => p.SendNotificationAsync(athleteA, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockPushNotificationService.Verify(p => p.SendNotificationAsync(athleteB, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_AthleteWithSessionAndNoCheckIn_When_HandlerRunsTwice_Then_DoesNotSendDuplicateReminder_UsingRealDbTracker()
    {
        // arrange: base de datos sqlite en memoria para usar la persistencia real de wellness_reminders_sent
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var dbOptions = new DbContextOptionsBuilder<global::Infrastructure.Data.AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var mockCurrentUser = new Mock<ICurrentUser>();
        await using var dbContext = new global::Infrastructure.Data.AppDbContext(dbOptions, mockCurrentUser.Object);
        await dbContext.Database.EnsureCreatedAsync();

        var realTracker = new global::Infrastructure.Services.DbWellnessReminderTracker(dbContext);

        var pfId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        var today = new DateOnly(2026, 10, 16);

        var mockAthleteRepo = new Mock<IAthleteRepository>();
        mockAthleteRepo.Setup(r => r.GetDistinctPfIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { pfId });

        var mockSchedule = new Mock<ISessionScheduleService>();
        mockSchedule.Setup(s => s.GetAthletesWithScheduledSessionAsync(today, pfId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { athleteId });

        var mockCheckIn = new Mock<IWellnessCheckInService>();
        mockCheckIn.Setup(s => s.GetCompletedCheckInAthleteIdsAsync(
                It.IsAny<IReadOnlyList<Guid>>(), today, pfId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());

        var mockPush = new Mock<IPushNotificationService>();

        var handlerWithRealTracker = new SendDailyWellnessRemindersCommandHandler(
            mockAthleteRepo.Object,
            mockCurrentUser.Object,
            mockSchedule.Object,
            mockCheckIn.Object,
            realTracker,
            mockPush.Object
        );

        // act 1: primera ejecucion
        var firstResult = await handlerWithRealTracker.HandleAsync(new SendDailyWellnessRemindersCommand(today));

        // assert 1: se envia el recordatorio y se guarda en la base de datos real
        Assert.Equal(1, firstResult.RemindersSent);
        Assert.Equal(0, firstResult.SkippedAlreadySent);
        mockPush.Verify(p => p.SendNotificationAsync(athleteId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        // comprobamos que el registro existe efectivamente en la tabla wellness_reminders_sent
        var recordInDb = await dbContext.WellnessRemindersSent.FirstOrDefaultAsync(r => r.AthleteId == athleteId && r.Date == today);
        Assert.NotNull(recordInDb);

        // act 2: segunda ejecucion del worker ese mismo dia
        var secondResult = await handlerWithRealTracker.HandleAsync(new SendDailyWellnessRemindersCommand(today));

        // assert 2: no se envia duplicado porque consulta la tabla real
        Assert.Equal(0, secondResult.RemindersSent);
        Assert.Equal(1, secondResult.SkippedAlreadySent);

        // verificamos que la notificacion solo se envio UNA sola vez en total entre ambas ejecuciones
        mockPush.Verify(p => p.SendNotificationAsync(athleteId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}