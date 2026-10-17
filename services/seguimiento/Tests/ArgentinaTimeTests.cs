using Domain.Common;

namespace Seguimiento.Tests;

public class ArgentinaTimeTests
{
    [Fact]
    public void FromUtc_AtLateArgentinaNight_KeepsThePreviousDay()
    {
        // 2026-10-17 02:09 UTC = 2026-10-16 23:09 en Argentina: el bug reportado por Enzo.
        var utc = new DateTime(2026, 10, 17, 2, 9, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 10, 16), DateOnly.FromDateTime(ArgentinaTime.FromUtc(utc)));
    }

    [Fact]
    public void FromUtc_JustAfterUtcMidnight_StillPreviousDayInArgentina()
    {
        var utc = new DateTime(2026, 10, 17, 0, 30, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 10, 16), DateOnly.FromDateTime(ArgentinaTime.FromUtc(utc)));
    }

    [Fact]
    public void FromUtc_MidAfternoonUtc_SameDayInArgentina()
    {
        var utc = new DateTime(2026, 10, 17, 15, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 10, 17), DateOnly.FromDateTime(ArgentinaTime.FromUtc(utc)));
    }
}