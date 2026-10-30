using Domain.Services;
using Xunit;

namespace Tests;

public class EpleyCalculatorTests
{
    [Fact]
    public void Calculates1Rm_AndRoundsDownTo2Point5()
    {
        // 90 kg x 6 reps -> Epley: 90 * (1 + 6/30) = 108 kg -> redondeo hacia abajo a 2.5 kg = 107.5 kg
        var result = EpleyCalculator.CalculateConservative1Rm(90m, 6);

        Assert.NotNull(result);
        Assert.Equal(107.5m, result.Value);
    }

    [Fact]
    public void ReturnsNull_WhenRepsOverTen()
    {
        var result = EpleyCalculator.CalculateConservative1Rm(90m, 12);

        Assert.Null(result);
    }

    [Fact]
    public void ReturnsNull_WhenRepsUnderOne()
    {
        var result = EpleyCalculator.CalculateConservative1Rm(90m, 0);

        Assert.Null(result);
    }

    [Fact]
    public void ReturnsNull_WhenWeightZeroOrNegative()
    {
        var resultZero = EpleyCalculator.CalculateConservative1Rm(0m, 5);
        var resultNegative = EpleyCalculator.CalculateConservative1Rm(-10m, 5);

        Assert.Null(resultZero);
        Assert.Null(resultNegative);
    }

    [Theory]
    [InlineData(100, 1, 102.5)] // 100 * (1 + 1/30) = 103.33 -> 102.5
    [InlineData(100, 10, 132.5)] // 100 * (1 + 10/30) = 133.33 -> 132.5
    [InlineData(80, 5, 92.5)] // 80 * (1 + 5/30) = 93.33 -> 92.5
    public void CalculatesVariousLoads(decimal weight, int reps, decimal expected)
    {
        var result = EpleyCalculator.CalculateConservative1Rm(weight, reps);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Value);
    }
}

