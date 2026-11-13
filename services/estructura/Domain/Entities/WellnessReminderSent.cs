using System;

namespace Domain.Entities;

public sealed class WellnessReminderSent
{
    public Guid Id { get; private set; }
    public Guid AthleteId { get; private set; }
    public DateOnly Date { get; private set; }
    public DateTime SentAt { get; private set; }

    private WellnessReminderSent() { }

    public static WellnessReminderSent Create(Guid athleteId, DateOnly date)
    {
        return new WellnessReminderSent
        {
            Id = Guid.NewGuid(),
            AthleteId = athleteId,
            Date = date,
            SentAt = DateTime.UtcNow
        };
    }
}
