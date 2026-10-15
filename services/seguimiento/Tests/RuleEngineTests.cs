using Domain.Entities;
using Infrastructure;
using Xunit;

namespace Seguimiento.Tests;

// Tests del motor heurístico del semáforo (KAN-363) y de su autonomía de datos (KAN-364):
// el color siempre sale de la evaluación local del check-in, sin depender de ningún servicio externo.
public class RuleEngineTests
{
    private readonly RuleEngine _engine = new();

    // ---------- Rojo: prioridad máxima ----------

    [Fact]
    public void Evaluate_ConLesionActiva_DeberiaDevolverRojoConMotivo()
    {
        var result = _engine.Evaluate("""{"hasInjury": true, "sleepHours": 7}""", "{}");

        Assert.Equal(TrafficLight.Red, result.TriggeredLight);
        Assert.Equal("Lesión activa detectada", result.Reason);
        Assert.Equal(20, result.ResultValue);
    }

    [Fact]
    public void Evaluate_ConHeridaActiva_DeberiaDevolverRojoConMotivo()
    {
        var result = _engine.Evaluate("""{"hasWound": true}""", "{}");

        Assert.Equal(TrafficLight.Red, result.TriggeredLight);
        Assert.Equal("Lesión activa detectada", result.Reason);
    }

    [Fact]
    public void Evaluate_ConLesionYPocoSueno_DeberiaPriorizarRojoSobreAmarillo()
    {
        // Una lesión activa anula cualquier otra señal: el orden de prioridad es Rojo → Amarillo → Verde.
        var result = _engine.Evaluate("""{"hasInjury": true, "sleepHours": 3, "effortRate": 9}""", "{}");

        Assert.Equal(TrafficLight.Red, result.TriggeredLight);
        Assert.Equal("Lesión activa detectada", result.Reason);
    }

    // ---------- Amarillo: señales de alerta ----------

    [Fact]
    public void Evaluate_ConPocoSuenoYEsfuerzoAlto_DeberiaDevolverAmarilloConTodasLasSenales()
    {
        var result = _engine.Evaluate("""{"sleepHours": 3, "effortRate": 9}""", "{}");

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Equal("Señales de alerta: esfuerzo alto, poco sueño", result.Reason);
        Assert.Equal(50, result.ResultValue);
    }

    [Fact]
    public void Evaluate_SinCheckIn_DeberiaDevolverAmarilloPorAusenciaDeDatos()
    {
        var result = _engine.Evaluate("", "{}");

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Equal("Señales de alerta: sin check-in", result.Reason);
    }

    [Fact]
    public void Evaluate_ConDolorNuevo_DeberiaDevolverAmarillo()
    {
        var result = _engine.Evaluate("""{"sleepHours": 8, "painZones": "Isquiotibial derecho"}""", "{}");

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Contains("dolor nuevo", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_ConEnfermedadEnElContexto_DeberiaDevolverAmarillo()
    {
        // El contexto también es local: no se consulta a ningún servicio para saber si el atleta está enfermo.
        var result = _engine.Evaluate("""{"sleepHours": 8}""", """{"isSick": true}""");

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Contains("enfermedad", result.Reason, StringComparison.Ordinal);
    }

    // ---------- Verde: estado óptimo ----------

    [Fact]
    public void Evaluate_EnEstadoOptimo_DeberiaDevolverVerde()
    {
        var result = _engine.Evaluate("""{"sleepHours": 8, "effortRate": 4, "isSick": false}""", "{}");

        Assert.Equal(TrafficLight.Green, result.TriggeredLight);
        Assert.Equal("Estado óptimo", result.Reason);
        Assert.Equal(90, result.ResultValue);
    }

    // ---------- Bordes y resiliencia a datos de entrada ----------

    [Theory]
    [InlineData(5.0, 7)] // en el umbral exacto: no es "poco sueño" ni "esfuerzo alto" (los umbrales son estrictos)
    [InlineData(8.0, 4)]
    public void Evaluate_ConValoresEnElUmbral_DeberiaDevolverVerde(double sleepHours, int effortRate)
    {
        var result = _engine.Evaluate($$"""{"sleepHours": {{sleepHours}}, "effortRate": {{effortRate}}}""", "{}");

        Assert.Equal(TrafficLight.Green, result.TriggeredLight);
    }

    [Theory]
    [InlineData("esto no es json {{{")]
    [InlineData("\"es un string, no un objeto\"")]
    public void Evaluate_ConJsonInvalido_NoDeberiaLanzarExcepcionYDebeCairEnAmarillo(string jsonInvalido)
    {
        // Un dato corrupto no puede tumbar el semáforo: se trata como ausencia de check-in.
        var result = _engine.Evaluate(jsonInvalido, "{}");

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Contains("sin check-in", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_SinParametros_DeberiaDevolverAmarilloSinExcepcion()
    {
        var result = _engine.Evaluate(null!, null!);

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
    }

    [Fact]
    public void Evaluate_DeberiaDevolverSiempreUnMotivoEnEspanol()
    {
        var luces = new[]
        {
            _engine.Evaluate("""{"hasInjury": true}""", "{}").Reason,
            _engine.Evaluate("""{"sleepHours": 3}""", "{}").Reason,
            _engine.Evaluate("""{"sleepHours": 8}""", "{}").Reason,
        };

        Assert.All(luces, motivo => Assert.False(string.IsNullOrWhiteSpace(motivo),
            "El semáforo siempre debe informar el motivo en español."));
    }
}
