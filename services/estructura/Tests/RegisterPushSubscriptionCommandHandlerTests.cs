using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Notifications.RegisterPushSubscription;
using Domain.Entities;
using Moq;
using Xunit;

namespace Estructura.Tests;

public class RegisterPushSubscriptionCommandHandlerTests
{
    private readonly Mock<IPushSubscriptionRepository> _mockSubscriptionRepo = new();
    private readonly Mock<IAthleteRepository> _mockAthleteRepo = new();
    private readonly Mock<ICurrentUser> _mockCurrentUser = new();
    private readonly Mock<IUnitOfWork> _mockUnitOfWork = new();
    private readonly RegisterPushSubscriptionCommandHandler _handler;

    public RegisterPushSubscriptionCommandHandlerTests()
    {
        _handler = new RegisterPushSubscriptionCommandHandler(
            _mockSubscriptionRepo.Object,
            _mockAthleteRepo.Object,
            _mockCurrentUser.Object,
            _mockUnitOfWork.Object);
    }

    [Fact]
    public async Task Given_AuthenticatedAthlete_When_Register_Then_CreatesSubscriptionWithAthleteId()
    {
        // arrange
        var userId = Guid.NewGuid();
        var athlete = Athlete.CreateAdult("Lucas", "Perez", "lucas@example.com", "1122334455");
        athlete.LinkAccount(userId);

        _mockCurrentUser.Setup(u => u.UserId).Returns(userId);
        _mockAthleteRepo.Setup(r => r.GetByAccountIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(athlete);

        _mockSubscriptionRepo.Setup(r => r.GetByEndpointAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PushSubscription?)null);

        var command = new RegisterPushSubscriptionCommand(
            "https://fcm.googleapis.com/fcm/send/test-endpoint",
            "p256dh-key-test",
            "auth-secret-test",
            "Chrome-Mobile");

        // act
        await _handler.HandleAsync(command);

        // assert
        _mockSubscriptionRepo.Verify(r => r.AddAsync(It.Is<PushSubscription>(s =>
            s.UserId == userId &&
            s.AthleteId == athlete.Id &&
            s.Endpoint == command.Endpoint &&
            s.P256dh == command.P256dh &&
            s.Auth == command.Auth &&
            s.UserAgent == command.UserAgent &&
            s.IsActive), It.IsAny<CancellationToken>()), Times.Once);

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_AuthenticatedCoach_When_Register_Then_CreatesSubscriptionWithoutAthleteId()
    {
        // arrange
        var coachUserId = Guid.NewGuid();
        _mockCurrentUser.Setup(u => u.UserId).Returns(coachUserId);
        _mockAthleteRepo.Setup(r => r.GetByAccountIdAsync(coachUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Athlete?)null);

        _mockSubscriptionRepo.Setup(r => r.GetByEndpointAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PushSubscription?)null);

        var command = new RegisterPushSubscriptionCommand(
            "https://updates.push.services.mozilla.com/wpush/v2/test-endpoint",
            "p256dh-coach",
            "auth-coach");

        // act
        await _handler.HandleAsync(command);

        // assert
        _mockSubscriptionRepo.Verify(r => r.AddAsync(It.Is<PushSubscription>(s =>
            s.UserId == coachUserId &&
            s.AthleteId == null &&
            s.Endpoint == command.Endpoint), It.IsAny<CancellationToken>()), Times.Once);

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_ExistingSubscriptionForSameUser_When_Register_Then_UpdatesKeysAndDoesNotDuplicate()
    {
        // arrange
        var userId = Guid.NewGuid();
        var existing = PushSubscription.Create(userId, null, "https://push.example.com/123", "old-key", "old-auth");

        _mockCurrentUser.Setup(u => u.UserId).Returns(userId);
        _mockAthleteRepo.Setup(r => r.GetByAccountIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Athlete?)null);

        _mockSubscriptionRepo.Setup(r => r.GetByEndpointAsync("https://push.example.com/123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var command = new RegisterPushSubscriptionCommand(
            "https://push.example.com/123",
            "new-p256dh",
            "new-auth",
            "Updated-Agent");

        // act
        await _handler.HandleAsync(command);

        // assert
        _mockSubscriptionRepo.Verify(r => r.AddAsync(It.IsAny<PushSubscription>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockSubscriptionRepo.Verify(r => r.UpdateAsync(It.Is<PushSubscription>(s =>
            s.Endpoint == command.Endpoint &&
            s.P256dh == "new-p256dh" &&
            s.Auth == "new-auth" &&
            s.UserAgent == "Updated-Agent"), It.IsAny<CancellationToken>()), Times.Once);

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_ExistingSubscriptionForDifferentUser_When_Register_Then_ReassignsToNewUser()
    {
        // arrange: un dispositivo que antes usaba otro usuario
        var oldUserId = Guid.NewGuid();
        var newUserId = Guid.NewGuid();
        var existing = PushSubscription.Create(oldUserId, null, "https://push.example.com/shared-device", "k1", "a1");

        _mockCurrentUser.Setup(u => u.UserId).Returns(newUserId);
        _mockAthleteRepo.Setup(r => r.GetByAccountIdAsync(newUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Athlete?)null);

        _mockSubscriptionRepo.Setup(r => r.GetByEndpointAsync("https://push.example.com/shared-device", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var command = new RegisterPushSubscriptionCommand(
            "https://push.example.com/shared-device",
            "new-k",
            "new-a");

        // act
        await _handler.HandleAsync(command);

        // assert
        _mockSubscriptionRepo.Verify(r => r.UpdateAsync(It.Is<PushSubscription>(s =>
            s.UserId == newUserId &&
            s.P256dh == "new-k" &&
            s.Auth == "new-a"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_UnauthenticatedUser_When_Register_Then_ThrowsUnauthorizedAccessException()
    {
        // arrange
        _mockCurrentUser.Setup(u => u.UserId).Returns((Guid?)null);

        var command = new RegisterPushSubscriptionCommand("https://push.example.com/test", "k", "a");

        // act & assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _handler.HandleAsync(command));
    }
}
