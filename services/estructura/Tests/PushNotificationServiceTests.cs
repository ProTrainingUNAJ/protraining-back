using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Persistence;
using DomainPushSubscription = Domain.Entities.PushSubscription;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using WebPush;
using Xunit;

namespace Estructura.Tests;

public class PushNotificationServiceTests
{
    private readonly Mock<IPushSubscriptionRepository> _mockSubscriptionRepo = new();
    private readonly Mock<IUnitOfWork> _mockUnitOfWork = new();
    private readonly Mock<IWebPushClientWrapper> _mockWebPushClient = new();
    private readonly Mock<ILogger<PushNotificationService>> _mockLogger = new();
    private readonly IOptions<VapidOptions> _vapidOptions;
    private readonly PushNotificationService _service;

    public PushNotificationServiceTests()
    {
        _vapidOptions = Microsoft.Extensions.Options.Options.Create(new VapidOptions
        {
            Subject = "mailto:test@protraining.app",
            PublicKey = "BKdJWX-W5-ybxqXlLV85tGKQ2pwGcbj_L9RkLRoDcbBfISRe-qYuFHV1qz5qLIGga7lriZEBRYWGaKJzxogdU6s",
            PrivateKey = "9N3cywi7eeq5wpLmHZYPucIGuP2_1jNSm6auEJuLHIc"
        });

        _service = new PushNotificationService(
            _mockSubscriptionRepo.Object,
            _mockUnitOfWork.Object,
            _vapidOptions,
            _mockWebPushClient.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task Given_NoSubscriptionsForAthlete_When_SendNotification_Then_DoesNotCallWebPushClient()
    {
        // arrange
        var athleteId = Guid.NewGuid();
        _mockSubscriptionRepo.Setup(r => r.GetActiveByAthleteIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DomainPushSubscription>());

        // act
        await _service.SendNotificationAsync(athleteId, "Título", "Mensaje");

        // assert
        _mockWebPushClient.Verify(c => c.SendNotificationAsync(
            It.IsAny<WebPush.PushSubscription>(),
            It.IsAny<string>(),
            It.IsAny<VapidDetails>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_ActiveSubscriptionsAndValidVapid_When_SendNotification_Then_DispatchesPayloadAndRecordsDelivery()
    {
        // arrange
        var athleteId = Guid.NewGuid();
        var sub = DomainPushSubscription.Create(Guid.NewGuid(), athleteId, "https://fcm.googleapis.com/test", "k1", "a1");

        _mockSubscriptionRepo.Setup(r => r.GetActiveByAthleteIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { sub });

        // act
        await _service.SendNotificationAsync(athleteId, "Recordatorio", "Completá el check-in");

        // assert
        _mockWebPushClient.Verify(c => c.SendNotificationAsync(
            It.Is<WebPush.PushSubscription>(p => p.Endpoint == sub.Endpoint),
            It.Is<string>(payload => payload.Contains("Recordatorio") && payload.Contains("Completá el check-in")),
            It.IsAny<VapidDetails>(),
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.NotNull(sub.LastDeliveredAt);
        _mockSubscriptionRepo.Verify(r => r.UpdateAsync(sub, It.IsAny<CancellationToken>()), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_ExpiredSubscriptionWith410Gone_When_SendNotification_Then_DeactivatesSubscription()
    {
        // arrange
        var athleteId = Guid.NewGuid();
        var sub = DomainPushSubscription.Create(Guid.NewGuid(), athleteId, "https://fcm.googleapis.com/expired", "k1", "a1");

        _mockSubscriptionRepo.Setup(r => r.GetActiveByAthleteIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { sub });

        var webPushSub = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.Gone);

        _mockWebPushClient.Setup(c => c.SendNotificationAsync(
                It.IsAny<WebPush.PushSubscription>(),
                It.IsAny<string>(),
                It.IsAny<VapidDetails>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebPushException("Subscription has expired or is no longer valid", webPushSub, httpResponse));

        // act
        await _service.SendNotificationAsync(athleteId, "Título", "Mensaje");

        // assert: la suscripcion queda desactivada inmediatamente
        Assert.False(sub.IsActive);
        _mockSubscriptionRepo.Verify(r => r.UpdateAsync(sub, It.IsAny<CancellationToken>()), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_ExpiredSubscriptionWith404NotFound_When_SendNotification_Then_DeactivatesSubscription()
    {
        // arrange
        var athleteId = Guid.NewGuid();
        var sub = DomainPushSubscription.Create(Guid.NewGuid(), athleteId, "https://fcm.googleapis.com/notfound", "k1", "a1");

        _mockSubscriptionRepo.Setup(r => r.GetActiveByAthleteIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { sub });

        var webPushSub = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.NotFound);

        _mockWebPushClient.Setup(c => c.SendNotificationAsync(
                It.IsAny<WebPush.PushSubscription>(),
                It.IsAny<string>(),
                It.IsAny<VapidDetails>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebPushException("Endpoint not found", webPushSub, httpResponse));

        // act
        await _service.SendNotificationAsync(athleteId, "Título", "Mensaje");

        // assert
        Assert.False(sub.IsActive);
        _mockSubscriptionRepo.Verify(r => r.UpdateAsync(sub, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_TransientError500_When_SendNotification_Then_DoesNotDeactivateSubscription()
    {
        // arrange
        var athleteId = Guid.NewGuid();
        var sub = DomainPushSubscription.Create(Guid.NewGuid(), athleteId, "https://fcm.googleapis.com/servererror", "k1", "a1");

        _mockSubscriptionRepo.Setup(r => r.GetActiveByAthleteIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { sub });

        var webPushSub = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        _mockWebPushClient.Setup(c => c.SendNotificationAsync(
                It.IsAny<WebPush.PushSubscription>(),
                It.IsAny<string>(),
                It.IsAny<VapidDetails>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebPushException("Internal Server Error", webPushSub, httpResponse));

        // act
        await _service.SendNotificationAsync(athleteId, "Título", "Mensaje");

        // assert: no se desactiva ante errores temporales
        Assert.True(sub.IsActive);
    }
}
