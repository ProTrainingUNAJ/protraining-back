using ProTraining.Common;
using ProTraining.Domain.Services;
using Xunit;

namespace ProTraining.UnitTests;

public class CalculationEngineTests
{
    [Theory]
    [InlineData(60, 80, 47.5)]  // 48 kg redondeado al múltiplo más cercano de 2.5 kg
    [InlineData(100, 75, 75.0)] // 75 kg exacto
    [InlineData(85, 70, 60.0)]  // 85 * 0.7 = 59.5 -> redondeado a 60.0 kg
    [InlineData(50, 90, 45.0)]  // 45 kg exacto
    public void CalculatePrescribedWeight_ShouldRoundToPlatesOf2Point5Kg(decimal record, decimal percentage, decimal expected)
    {
        // Act
        decimal result = CalculationEngine.CalculatePrescribedWeight(record, percentage);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(80, 1, 80.0)]   // 1 repetición: el 1RM es el peso directo
    [InlineData(80, 6, 95.0)]   // 80 * (1 + 6/30) = 96 kg -> redondeado a 95 kg
    [InlineData(100, 10, 132.5)] // 100 * (1 + 10/30) = 133.33 kg -> redondeado a 132.5 kg
    public void EstimateOneRepMaxEpley_ShouldCalculateCorrectly(decimal weight, int reps, decimal expected)
    {
        // Act
        decimal estimated = CalculationEngine.EstimateOneRepMaxEpley(weight, reps);

        // Assert
        Assert.Equal(expected, estimated);
    }

    [Fact]
    public void CalculatePrescribedWeight_WithInvalidPercentage_ShouldThrowDomainException()
    {
        Assert.Throws<DomainException>(() => CalculationEngine.CalculatePrescribedWeight(100, -10));
        Assert.Throws<DomainException>(() => CalculationEngine.CalculatePrescribedWeight(100, 160));
    }
}
