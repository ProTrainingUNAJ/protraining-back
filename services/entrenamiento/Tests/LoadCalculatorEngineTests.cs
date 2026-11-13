using Domain.Entities;
using Domain.Services;

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
        Assert.Equal(72.5m, resultEstimado.WeightKg); // (100 * 0.80) * 0.90 = 72.0 -> redondeado a discos de 2.5 kg = 72.5
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
    [InlineData(80.0, 80.0)]
    [InlineData(81.24, 80.0)]
    [InlineData(81.25, 82.5)]
    [InlineData(82.5, 82.5)]
    [InlineData(83.74, 82.5)]
    [InlineData(83.75, 85.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(1.24, 0.0)]
    [InlineData(1.25, 2.5)]
    public void RoundToNearestPlate_DeberiaRedondearASaltosDeDiscosDeDosPuntoCincoKg(decimal theoreticalWeight, decimal expectedWeight)
    {
        var result = _engine.RoundToNearestPlate(theoreticalWeight);

        Assert.Equal(expectedWeight, result);
    }

    [Theory]
    [InlineData(80.0, 30.0)]
    [InlineData(20.0, 0.0)]
    [InlineData(15.0, 0.0)]
    [InlineData(102.5, 41.25)]
    public void CalculateWeightPerSide_ConBarraEstandar_DeberiaCalcularPesoPorLado(double totalWeight, double expectedWeightPerSide)
    {
        var result = _engine.CalculateWeightPerSide(totalWeight);
        Assert.Equal(expectedWeightPerSide, result);
    }

    [Fact]
    public void CalculateWeightPerSide_SinPesoTotal_DeberiaDevolverNull()
    {
        var result = _engine.CalculateWeightPerSide(null);
        Assert.Null(result);
    }

    [Fact]
    public void CalculateTimeTarget_CriterioAceptacion_AtletaCon100mEn14sYSerieAl90PorCiento_DeberiaDevolver15Punto6s()
    {
        // DADO un atleta con 100 m en 14 s y una serie al 90 %
        decimal bestTime = 14m;
        decimal percentage = 90m;

        // CUANDO se calcula
        var target = _engine.CalculateTimeTarget(bestTime, percentage);

        // ENTONCES su meta es 15,6 s
        Assert.Equal(15.6m, target);
    }

    [Fact]
    public void CalculateTimeTarget_SinTest_DeberiaDevolverNullParaMostrarSerieSinMeta()
    {
        var target = _engine.CalculateTimeTarget(null, 90m);
        Assert.Null(target);
    }

    [Theory]
    [InlineData(14.0, 90.0, 15.6)]
    [InlineData(14.0, 100.0, 14.0)]
    [InlineData(14.0, 80.0, 17.5)]
    [InlineData(12.0, 90.0, 13.3)]
    public void CalculateTimeTarget_DiferentesMarcasEIntensidades_CalculaYRedondeaAUnDecimal(
        decimal bestTime, decimal percentage, decimal expectedTarget)
    {
        var target = _engine.CalculateTimeTarget(bestTime, percentage);
        Assert.Equal(expectedTarget, target);
    }
}