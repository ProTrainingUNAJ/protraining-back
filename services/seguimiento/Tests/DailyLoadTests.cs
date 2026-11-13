using Domain.Entities;

namespace Seguimiento.Tests;

public class DailyLoadTests
{
    [Fact]
    public void Given_60Minutes_Rpe7_When_TotalLoad_Then_420()
    {
        var load = new DailyLoad { GymMinutes = 60, GymRpe = 7 };

        Assert.Equal(420, load.TotalLoad);
    }

    [Fact]
    public void Given_MultipleActivities_When_TotalLoad_Then_SumOfMinutesTimesRpe()
    {
        var load = new DailyLoad
        {
            GymMinutes = 60, GymRpe = 7,
            Match1Minutes = 90, Match1Rpe = 8,
        };

        Assert.Equal(1140, load.TotalLoad);
    }

    [Fact]
    public void Given_ActivityWithMinutesAndNoRpe_When_TotalLoad_Then_DoesNotSum()
    {
        var load = new DailyLoad { GymMinutes = 60, GymRpe = null };

        Assert.Equal(0, load.TotalLoad);
    }

    [Fact]
    public void Given_AllZero_When_TotalLoad_Then_0()
    {
        var load = new DailyLoad();

        Assert.Equal(0, load.TotalLoad);
    }
}