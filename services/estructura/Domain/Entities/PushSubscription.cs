using System;
using Domain.Common;

namespace Domain.Entities;

public sealed class PushSubscription : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid? AthleteId { get; private set; }
    public string Endpoint { get; private set; } = string.Empty;
    public string P256dh { get; private set; } = string.Empty;
    public string Auth { get; private set; } = string.Empty;
    public string? UserAgent { get; private set; }
    public DateTime? LastDeliveredAt { get; private set; }
    public bool IsActive { get; private set; } = true;

    private PushSubscription() { }

    public static PushSubscription Create(
        Guid userId,
        Guid? athleteId,
        string endpoint,
        string p256dh,
        string auth,
        string? userAgent = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("El identificador de usuario es obligatorio.", nameof(userId));

        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("El endpoint de suscripción es obligatorio.", nameof(endpoint));

        if (string.IsNullOrWhiteSpace(p256dh))
            throw new ArgumentException("La clave pública p256dh es obligatoria.", nameof(p256dh));

        if (string.IsNullOrWhiteSpace(auth))
            throw new ArgumentException("La clave de autenticación auth es obligatoria.", nameof(auth));

        return new PushSubscription
        {
            UserId = userId,
            AthleteId = athleteId,
            Endpoint = endpoint.Trim(),
            P256dh = p256dh.Trim(),
            Auth = auth.Trim(),
            UserAgent = userAgent?.Trim(),
            IsActive = true
        };
    }

    public void UpdateKeys(string p256dh, string auth, string? userAgent = null)
    {
        if (string.IsNullOrWhiteSpace(p256dh))
            throw new ArgumentException("La clave pública p256dh no puede estar vacía.", nameof(p256dh));

        if (string.IsNullOrWhiteSpace(auth))
            throw new ArgumentException("La clave de autenticación auth no puede estar vacía.", nameof(auth));

        P256dh = p256dh.Trim();
        Auth = auth.Trim();

        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            UserAgent = userAgent.Trim();
        }

        IsActive = true;
    }

    public void AssignToUser(Guid newUserId, Guid? newAthleteId)
    {
        if (newUserId == Guid.Empty)
            throw new ArgumentException("El identificador de usuario es obligatorio.", nameof(newUserId));

        UserId = newUserId;
        AthleteId = newAthleteId;
        IsActive = true;
    }

    public void RecordDelivery(DateTime deliveredAtUtc)
    {
        LastDeliveredAt = deliveredAtUtc;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Reactivate()
    {
        IsActive = true;
    }
}
