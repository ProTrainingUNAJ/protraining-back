using Domain.Entities;
using Domain.Exceptions;
using Domain.Services;
using Xunit;

namespace Tests;

public class EpleyCalculatorTests
{
    [Fact]
    public void Calculate_Given85KgAnd8Reps_ReturnsEstimatedAndProposal()
    {
        // 85 kg x 8 reps:
        // Epley 1RM = 85 * (1 + 8/30) = 107.666... kg (~107.67 kg con error < 0.1 kg).
        // Escalón conservador (múltiplo de 2.5 kg hacia abajo): 43 * 2.5 = 107.5 kg.
        // (Nota: el detalle de la tarea mencionaba 105 kg por error manual de cálculo; el múltiplo de 2.5 kg inmediatamente inferior a 107.67 kg es 107.5 kg).
        var estimated = EpleyCalculator.CalculateEstimated1Rm(85m, 8);
        var proposal = EpleyCalculator.CalculateConservative1Rm(85m, 8);

        Assert.NotNull(estimated);
        Assert.True(Math.Abs(estimated.Value - 107.67m) < 0.1m);
        Assert.NotNull(proposal);
        Assert.Equal(107.5m, proposal.Value);
    }

    [Theory]
    [InlineData(85, 8, 107.67, 107.5)]
    [InlineData(90, 6, 108.0, 107.5)]
    [InlineData(100, 1, 103.33, 102.5)]
    [InlineData(60, 10, 80.0, 80.0)]
    public void Calculate_TableCases_ReturnsExpected1RmAndProposal(
        decimal weight, int reps, decimal expectedEstimated, decimal expectedProposal)
    {
        var estimated = EpleyCalculator.CalculateEstimated1Rm(weight, reps);
        var proposal = EpleyCalculator.CalculateConservative1Rm(weight, reps);

        Assert.NotNull(estimated);
        Assert.True(Math.Abs(estimated.Value - expectedEstimated) < 0.1m);
        Assert.NotNull(proposal);
        Assert.Equal(expectedProposal, proposal.Value);
    }

    [Fact]
    public void Calculate_WhenRepsOverTen_ReturnsNull()
    {
        // 90 kg x 11 reps -> No se calcula (solo válido de 1 a 10 repeticiones)
        var estimated = EpleyCalculator.CalculateEstimated1Rm(90m, 11);
        var proposal = EpleyCalculator.CalculateConservative1Rm(90m, 11);

        Assert.Null(estimated);
        Assert.Null(proposal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Calculate_WhenWeightZeroOrNegative_ThrowsDomainException(decimal weight)
    {
        var exEst = Assert.Throws<DomainException>(() => EpleyCalculator.CalculateEstimated1Rm(weight, 5));
        var exCons = Assert.Throws<DomainException>(() => EpleyCalculator.CalculateConservative1Rm(weight, 5));

        Assert.Equal("El peso debe ser mayor a 0 kg.", exEst.Message);
        Assert.Equal("El peso debe ser mayor a 0 kg.", exCons.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Calculate_WhenRepsZeroOrNegative_ThrowsDomainException(int reps)
    {
        var exEst = Assert.Throws<DomainException>(() => EpleyCalculator.CalculateEstimated1Rm(90m, reps));
        var exCons = Assert.Throws<DomainException>(() => EpleyCalculator.CalculateConservative1Rm(90m, reps));

        Assert.Equal("Las repeticiones deben ser mayores a 0.", exEst.Message);
        Assert.Equal("Las repeticiones deben ser mayores a 0.", exCons.Message);
    }

    [Theory]
    [InlineData(107.5, 107.5)] // Propuesta igual al récord vigente -> no es mayor
    [InlineData(110.0, 107.5)] // Propuesta menor al récord vigente
    public void Proposal_WhenLessOrEqualToRecord_ThrowsDomainException(decimal currentRecord, decimal proposedWeight)
    {
        var ex = Assert.Throws<DomainException>(() =>
            RecordProposal.Create(Guid.NewGuid(), Guid.NewGuid(), currentRecord, proposedWeight, 90m, 6));

        Assert.Equal("El peso propuesto debe ser mayor al récord vigente.", ex.Message);
    }
}
