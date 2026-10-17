namespace Application.DTOs.Notifications;

public sealed record PushSubscriptionKeys(
    string P256dh,
    string Auth);

public sealed record RegisterPushSubscriptionRequest(
    string Endpoint,
    PushSubscriptionKeys Keys,
    string? UserAgent = null);
