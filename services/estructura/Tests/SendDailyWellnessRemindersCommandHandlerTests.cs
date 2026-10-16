using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Services;
using Application.UseCases.Reminders.SendDailyWellnessReminders;
using Moq;
using Xunit;

namespace Estructura.Tests;

public class SendDailyWellnessRemindersCommandHandlerTests
{
    private readonly Mock<ISessionScheduleService> _mockSessionScheduleService;
    private readonly Mock<IWellnessCheckInService> _mockWellnessCheckInService;
    private readonly Mock<IWellnessReminderTracker> _mockReminderTracker;
    private readonly Mock<IPushNotificationService> _mockPushNotificationService;
    private readonly SendDailyWellnessRemindersCommandHandler _handler;

    public SendDailyWellnessRemindersCommandHandlerTests()
    {
        _mockSessionScheduleService = new Mock<ISessionScheduleService>();
        _mockWellnessCheckInService = new Mock<IWellnessCheckInService>();
        _mockReminderTracker = new Mock<IWellnessReminderTracker>();
        _mockPushNotificationService = new Mock<IPushNotificationService>();

        _handler = new SendDailyWellnessRemindersCommandHandler(
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

        _mockSessionScheduleService.Setup(s => s.GetAthletesWithScheduledSessionAsync(today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { athleteId });

        _mockWellnessCheckInService.Setup(s => s.HasCompletedCheckInAsync(athleteId, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

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

        _mockSessionScheduleService.Setup(s => s.GetAthletesWithScheduledSessionAsync(today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { athleteId });

        _mockWellnessCheckInService.Setup(s => s.HasCompletedCheckInAsync(athleteId, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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

        _mockSessionScheduleService.Setup(s => s.GetAthletesWithScheduledSessionAsync(today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { athleteId });

        _mockWellnessCheckInService.Setup(s => s.HasCompletedCheckInAsync(athleteId, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

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

        _mockSessionScheduleService.Setup(s => s.GetAthletesWithScheduledSessionAsync(today, It.IsAny<CancellationToken>()))
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
}
