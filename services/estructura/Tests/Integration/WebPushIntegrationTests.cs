using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.DTOs.Athletes;
using Application.DTOs.Notifications;
using Application.Interfaces.Services;
using Domain.Entities;
using Estructura.Tests.Infrastructure;
using Infrastructure.Data;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebPush;

namespace Estructura.Tests.Integration;

public sealed class FakeWebPushClientWrapper : IWebPushClientWrapper
{
    public sealed record DispatchedNotification(
        WebPush.PushSubscription Subscription,
        string Payload,
        VapidDetails VapidDetails);

    private readonly ConcurrentBag<DispatchedNotification> _dispatches = new();
    private Func<WebPush.PushSubscription, string, Task>? _interceptor;

    public IReadOnlyList<DispatchedNotification> Dispatches => _dispatches.ToList();

    public void SetupThrow(Exception ex)
    {
        _interceptor = (_, _) => Task.FromException(ex);
    }

    public Task SendNotificationAsync(
        WebPush.PushSubscription subscription,
        string payload,
        VapidDetails vapidDetails,
        CancellationToken cancellationToken = default)
    {
        if (_interceptor is not null)
        {
            return _interceptor(subscription, payload);
        }

        _dispatches.Add(new DispatchedNotification(subscription, payload, vapidDetails));
        return Task.CompletedTask;
    }
}

public sealed class WebPushIntegrationTests : IAsyncDisposable
{
    private readonly FakeWebPushClientWrapper _fakeClient = new();
    private readonly EstructuraTestApplicationFactory _factory;
    private readonly Guid _pfAId = Guid.NewGuid();
    private readonly Guid _pfBId = Guid.NewGuid();

    public WebPushIntegrationTests()
    {
        _factory = new EstructuraTestApplicationFactory(_fakeClient);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Given_AthleteWithValidSubscription_When_NotificationDispatched_Then_SimulatedDispatchOccursWithCorrectPayloadAndPersistsDelivery()
    {
        // 1. Arrange: Crear Atleta y registrar suscripción Web Push vía endpoint real
        var userAthleteId = Guid.NewGuid();
        var athlete = await CreateAthleteInDbAsync(_pfAId, "Santiago", "Gómez", "santiago.gomez@test.com", userAthleteId);

        var athleteClient = _factory.CreateAuthenticatedClient(_pfAId, role: "Atleta", userId: userAthleteId);
        var subRequest = new RegisterPushSubscriptionRequest(
            Endpoint: "https://fcm.googleapis.com/fcm/send/test-token-wp01",
            Keys: new PushSubscriptionKeys(
                P256dh: "BMb5i7V2sZ0jQ5f7L1vY4g==",
                Auth: "5g1B8kX=="),
            UserAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ProTraining PWA");

        var registerResponse = await athleteClient.PostAsJsonAsync("/api/v1/push-subscriptions", subRequest);
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        // 2. Act: Despachar notificación mediante el servicio de notificaciones
        using (var scope = _factory.CreateScope())
        {
            var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
            await pushService.SendNotificationAsync(athlete.Id, "Recordatorio", "Completá el check-in diario");
        }

        // 3. Assert: Despacho simulado con datos correctos
        Assert.Single(_fakeClient.Dispatches);
        var dispatch = _fakeClient.Dispatches[0];

        Assert.Equal("https://fcm.googleapis.com/fcm/send/test-token-wp01", dispatch.Subscription.Endpoint);
        Assert.Equal("BMb5i7V2sZ0jQ5f7L1vY4g==", dispatch.Subscription.P256DH);
        Assert.Equal("5g1B8kX==", dispatch.Subscription.Auth);

        using var payloadDoc = JsonDocument.Parse(dispatch.Payload);
        var root = payloadDoc.RootElement;
        Assert.Equal("Recordatorio", root.GetProperty("title").GetString());
        Assert.Equal("Completá el check-in diario", root.GetProperty("body").GetString());
        Assert.Equal("/athlete/check-in", root.GetProperty("url").GetString());
        Assert.Equal("wellness-reminder", root.GetProperty("tag").GetString());

        // 4. Assert: Persistencia real del estado de entrega en la base de datos de pruebas
        using (var db = _factory.CreateDbContext())
        {
            var savedSub = await db.PushSubscriptions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Endpoint == "https://fcm.googleapis.com/fcm/send/test-token-wp01");

            Assert.NotNull(savedSub);
            Assert.True(savedSub.IsActive);
            Assert.NotNull(savedSub.LastDeliveredAt);
            Assert.Equal(athlete.Id, savedSub.AthleteId);
        }
    }

    [Fact]
    public async Task Given_AthleteWithMultipleDevices_When_NotificationDispatched_Then_AllSubscriptionsReceiveNotificationWithoutCrossDispatches()
    {
        // 1. Arrange: Atleta con dos dispositivos registrados (móvil y escritorio)
        var userAthleteId = Guid.NewGuid();
        var athlete = await CreateAthleteInDbAsync(_pfAId, "Joaquín", "Silva", "joaquin.silva@test.com", userAthleteId);

        var athleteClient = _factory.CreateAuthenticatedClient(_pfAId, role: "Atleta", userId: userAthleteId);

        var mobileSub = new RegisterPushSubscriptionRequest(
            Endpoint: "https://fcm.googleapis.com/fcm/send/device-mobile-wp02",
            Keys: new PushSubscriptionKeys("p256dh-mobile", "auth-mobile"),
            UserAgent: "ProTraining-Mobile-PWA");

        var desktopSub = new RegisterPushSubscriptionRequest(
            Endpoint: "https://fcm.googleapis.com/fcm/send/device-desktop-wp02",
            Keys: new PushSubscriptionKeys("p256dh-desktop", "auth-desktop"),
            UserAgent: "ProTraining-Desktop-Browser");

        var res1 = await athleteClient.PostAsJsonAsync("/api/v1/push-subscriptions", mobileSub);
        var res2 = await athleteClient.PostAsJsonAsync("/api/v1/push-subscriptions", desktopSub);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        // 2. Act: Despachar notificación
        using (var scope = _factory.CreateScope())
        {
            var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
            await pushService.SendNotificationAsync(athlete.Id, "Aviso General", "Rutina modificada");
        }

        // 3. Assert: Se realizaron exactamente los 2 despachos correspondientes al usuario
        Assert.Equal(2, _fakeClient.Dispatches.Count);
        var endpoints = _fakeClient.Dispatches.Select(d => d.Subscription.Endpoint).ToList();
        Assert.Contains("https://fcm.googleapis.com/fcm/send/device-mobile-wp02", endpoints);
        Assert.Contains("https://fcm.googleapis.com/fcm/send/device-desktop-wp02", endpoints);

        // 4. Assert: Ambos registraron entrega en BD
        using (var db = _factory.CreateDbContext())
        {
            var subs = await db.PushSubscriptions.IgnoreQueryFilters()
                .Where(s => s.AthleteId == athlete.Id)
                .ToListAsync();

            Assert.Equal(2, subs.Count);
            Assert.All(subs, s => Assert.NotNull(s.LastDeliveredAt));
        }
    }

    [Fact]
    public async Task Given_AthleteWithoutSubscriptions_When_NotificationDispatched_Then_NoFakeDispatchOccursAndNoFalseDeliveryConfirmed()
    {
        // 1. Arrange: Atleta sin ninguna suscripción registrada
        var athlete = await CreateAthleteInDbAsync(_pfAId, "Sin", "Suscripciones", "sin.suscripciones@test.com", Guid.NewGuid());

        // 2. Act
        using (var scope = _factory.CreateScope())
        {
            var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
            await pushService.SendNotificationAsync(athlete.Id, "Recordatorio", "Mensaje de prueba");
        }

        // 3. Assert: El cliente de despacho no fue invocado
        Assert.Empty(_fakeClient.Dispatches);
    }

    [Fact]
    public async Task Given_SimulatedProviderReturnsTransient500_When_NotificationDispatched_Then_ErrorIsHandledAndSubscriptionRemainsActive()
    {
        // 1. Arrange: Suscripción registrada y proveedor simulado configurado para fallar con 500
        var userAthleteId = Guid.NewGuid();
        var athlete = await CreateAthleteInDbAsync(_pfAId, "Martín", "Benítez", "martin.benitez@test.com", userAthleteId);

        var athleteClient = _factory.CreateAuthenticatedClient(_pfAId, role: "Atleta", userId: userAthleteId);
        var subRequest = new RegisterPushSubscriptionRequest(
            Endpoint: "https://fcm.googleapis.com/fcm/send/error-500-wp04",
            Keys: new PushSubscriptionKeys("k500", "a500"));

        await athleteClient.PostAsJsonAsync("/api/v1/push-subscriptions", subRequest);

        var webPushSub = new WebPush.PushSubscription(subRequest.Endpoint, subRequest.Keys.P256dh, subRequest.Keys.Auth);
        var errorResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        _fakeClient.SetupThrow(new WebPushException("Internal Server Error", webPushSub, errorResponse));

        // 2. Act: Despachar notificación
        using (var scope = _factory.CreateScope())
        {
            var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
            // No debe lanzar excepción no controlada hacia el llamador
            await pushService.SendNotificationAsync(athlete.Id, "Alerta", "Mensaje transitorio");
        }

        // 3. Assert: En base de datos, la suscripción no fue desactivada ante un error transitorio
        using (var db = _factory.CreateDbContext())
        {
            var savedSub = await db.PushSubscriptions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Endpoint == "https://fcm.googleapis.com/fcm/send/error-500-wp04");

            Assert.NotNull(savedSub);
            Assert.True(savedSub.IsActive);
            Assert.Null(savedSub.LastDeliveredAt); // No se declara falsamente entregado
        }
    }

    [Fact]
    public async Task Given_SimulatedProviderReturnsGone410_When_NotificationDispatched_Then_SubscriptionIsDeactivatedInDatabase()
    {
        // 1. Arrange: Suscripción expirada
        var userAthleteId = Guid.NewGuid();
        var athlete = await CreateAthleteInDbAsync(_pfAId, "Expirado", "Gómez", "expirado@test.com", userAthleteId);

        var athleteClient = _factory.CreateAuthenticatedClient(_pfAId, role: "Atleta", userId: userAthleteId);
        var subRequest = new RegisterPushSubscriptionRequest(
            Endpoint: "https://fcm.googleapis.com/fcm/send/expired-410-wp05",
            Keys: new PushSubscriptionKeys("k410", "a410"));

        await athleteClient.PostAsJsonAsync("/api/v1/push-subscriptions", subRequest);

        var webPushSub = new WebPush.PushSubscription(subRequest.Endpoint, subRequest.Keys.P256dh, subRequest.Keys.Auth);
        var goneResponse = new HttpResponseMessage(HttpStatusCode.Gone);
        _fakeClient.SetupThrow(new WebPushException("Subscription expired", webPushSub, goneResponse));

        // 2. Act
        using (var scope = _factory.CreateScope())
        {
            var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
            await pushService.SendNotificationAsync(athlete.Id, "Aviso", "Mensaje");
        }

        // 3. Assert: La suscripción queda desactivada inmediatamente en base de datos
        using (var db = _factory.CreateDbContext())
        {
            var savedSub = await db.PushSubscriptions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Endpoint == "https://fcm.googleapis.com/fcm/send/expired-410-wp05");

            Assert.NotNull(savedSub);
            Assert.False(savedSub.IsActive);
        }
    }

    [Fact]
    public async Task Given_TwoSeparateTenantsPfAAndPfB_When_DispatchingForPfA_Then_NoSubscriptionsFromPfBAreSelectedOrNotified()
    {
        // 1. Arrange: PF-A y PF-B con atletas y suscripciones independientes
        var userAthleteAId = Guid.NewGuid();
        var athleteA = await CreateAthleteInDbAsync(_pfAId, "Atleta", "TenantA", "atleta.a@test.com", userAthleteAId);

        var userAthleteBId = Guid.NewGuid();
        var athleteB = await CreateAthleteInDbAsync(_pfBId, "Atleta", "TenantB", "atleta.b@test.com", userAthleteBId);

        var clientA = _factory.CreateAuthenticatedClient(_pfAId, role: "Atleta", userId: userAthleteAId);
        var clientB = _factory.CreateAuthenticatedClient(_pfBId, role: "Atleta", userId: userAthleteBId);

        var subA = new RegisterPushSubscriptionRequest("https://fcm.googleapis.com/fcm/send/tenant-a-sub", new PushSubscriptionKeys("ka", "aa"));
        var subB = new RegisterPushSubscriptionRequest("https://fcm.googleapis.com/fcm/send/tenant-b-sub", new PushSubscriptionKeys("kb", "ab"));

        await clientA.PostAsJsonAsync("/api/v1/push-subscriptions", subA);
        await clientB.PostAsJsonAsync("/api/v1/push-subscriptions", subB);

        // 2. Act: Despachar notificación para Atleta A (perteneciente a PF-A)
        using (var scope = _factory.CreateScope())
        {
            var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
            await pushService.SendNotificationAsync(athleteA.Id, "Mensaje Tenant A", "Solo para atleta de PF-A");
        }

        // 3. Assert: Únicamente la suscripción de Atleta A fue notificada
        Assert.Single(_fakeClient.Dispatches);
        Assert.Equal("https://fcm.googleapis.com/fcm/send/tenant-a-sub", _fakeClient.Dispatches[0].Subscription.Endpoint);

        // 4. Assert: En base de datos, la suscripción de Atleta B permanece sin entrega
        using (var db = _factory.CreateDbContext())
        {
            var savedSubB = await db.PushSubscriptions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Endpoint == "https://fcm.googleapis.com/fcm/send/tenant-b-sub");

            Assert.NotNull(savedSubB);
            Assert.Null(savedSubB.LastDeliveredAt);
            Assert.Equal(athleteB.Id, savedSubB.AthleteId);
        }
    }

    private async Task<Athlete> CreateAthleteInDbAsync(Guid pfId, string firstName, string lastName, string email, Guid accountId)
    {
        // Se crea el atleta directamente en el contexto autenticado de ese PF
        using var client = _factory.CreateAuthenticatedClient(pfId);
        var request = new CreateAthleteRequest(
            FirstName: firstName,
            LastName: lastName,
            Email: email,
            Phone: "+5491100000000",
            IsMinor: false);

        var response = await client.PostAsJsonAsync("/api/v1/athletes", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var athleteDto = await response.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athleteDto);

        // Asociamos el AccountId en la base de datos para que coincida con el usuario autenticado
        using var db = _factory.CreateDbContext();
        var athlete = await db.Athletes.IgnoreQueryFilters().FirstAsync(a => a.Id == athleteDto.Id);

        // Asignar account_id vía propiedad privada usando reflexión para la prueba
        var accountIdProperty = typeof(Athlete).GetProperty(nameof(Athlete.AccountId));
        accountIdProperty?.SetValue(athlete, accountId);
        await db.SaveChangesAsync();

        return athlete;
    }
}
