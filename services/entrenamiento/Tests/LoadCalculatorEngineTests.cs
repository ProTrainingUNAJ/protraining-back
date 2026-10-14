using Domain.Entities;
using Domain.Services;
using Xunit;

namespace Entrenamiento.Tests;

public class LoadCalculatorEngineTests
{
    private readonly LoadCalculatorEngine _engine = new();

    [Fact]
    public void CalculateLoad_ConRecordTesteado_DeberiaDevolverPorcentajeExacto()
    {
        //dado un récord testeado de 100 kg y una serie al 80 %
        decimal rm = 100m;
        decimal porcentaje = 80m;

        
        var result = _engine.CalculateLoad(rm, RecordSource.Tested, porcentaje);

        Assert.False(result.IsReference);
        Assert.Equal(80m, result.WeightKg);
    }

    [Fact]
    public void CalculateLoad_ConRecordEstimado_DeberiaDevolverPesoMasBajoQueTesteado()
    {
        decimal rm = 100m;
        decimal porcentaje = 80m;
   
        var resultTesteado = _engine.CalculateLoad(rm, RecordSource.Tested, porcentaje);
        var resultEstimado = _engine.CalculateLoad(rm, RecordSource.Estimated, porcentaje);

        Assert.False(resultEstimado.IsReference);
        Assert.True(resultEstimado.WeightKg < resultTesteado.WeightKg, "El peso estimado debe ser menor al testeado");
        Assert.Equal(75m, resultEstimado.WeightKg); // (100 * 0.80) * 0.95 = 76 -> redondeado a discos = 75
    }

    [Fact]
    public void CalculateLoad_SinRecord_DeberiaDevolverSerieDeReferencia()
    {
        var result = _engine.CalculateLoad(null, null, 80m);

        Assert.True(result.IsReference);
        Assert.Null(result.WeightKg);
    }

    [Theory]
    [InlineData(73.4, 72.5)]
    [InlineData(86.8, 87.5)]
    public void RoundToNearestPlate_DeberiaRedondearASaltosDeDiscosDeDosPuntoCincoKg(decimal theoreticalWeight, decimal expectedWeight)
    {
        var result = _engine.RoundToNearestPlate(theoreticalWeight);

        Assert.Equal(expectedWeight, result);
    }
}