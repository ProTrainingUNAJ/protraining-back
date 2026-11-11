using Domain.Entities;
using Domain.Services;
using Xunit;

namespace Seguimiento.Tests;

// Tests del motor heurístico del semáforo (KAN-363) y de su autonomía de datos (KAN-364):
// el color siempre sale de la evaluación local del check-in, sin depender de ningún servicio externo.
public class TrafficLightEngineTests
{
    private readonly TrafficLightEngine _engine = new();

    // ---------- Rojo: prioridad máxima ----------

    [Fact]
    public void Evaluate_WithActiveInjury_ReturnsRedWithLiteralReason()
    {
        var result = _engine.Evaluate("""{"hasInjury": true, "sleepHours": 8}""", "{}");

        Assert.Equal(TrafficLight.Red, result.TriggeredLight);
        Assert.Equal("Lesión activa", result.Reason);
        Assert.Equal(20, result.ResultValue);
    }

    [Fact]
    public void Evaluate_WithHasActiveInjuryJsonKey_ReturnsRed()
    {
        // Clave canónica del check-in real (columna has_active_injury).
        var result = _engine.Evaluate("""{"hasActiveInjury": true, "sleepHours": 8}""", "{}");

        Assert.Equal(TrafficLight.Red, result.TriggeredLight);
        Assert.Equal("Lesión activa", result.Reason);
    }

    [Fact]
    public void Evaluate_WithActiveInjuryAndModerateSleep_UnifiesAllReasonsInRedList()
    {
        // KAN-560: los motivos de Rojo y Amarillo se unifican en una sola lista por comas,
        // sin prefijo, nombrando todas las señales (el color más grave manda).
        var result = _engine.Evaluate("""{"hasInjury": true, "sleepHours": 7}""", "{}");

        Assert.Equal(TrafficLight.Red, result.TriggeredLight);
        Assert.Equal("Lesión activa, Sueño moderado", result.Reason);
        Assert.Equal(20, result.ResultValue);
    }

    [Fact]
    public void Evaluate_WithActiveInjuryInTypedData_ReturnsRed()
    {
        // Camino del check-in real: el caso de uso entrega CheckInData ya tipado, sin pasar por JSON.
        var result = _engine.Evaluate(
            new CheckInData { HasActiveInjury = true, SleepHours = 8, IsSick = false },
            context: null);

        Assert.Equal(TrafficLight.Red, result.TriggeredLight);
        Assert.Equal("Lesión activa", result.Reason);
        Assert.Equal(20, result.ResultValue);
    }

    [Fact]
    public void Evaluate_WithActiveWound_ReturnsRedWithReason()
    {
        var result = _engine.Evaluate("""{"hasWound": true}""", "{}");

        Assert.Equal(TrafficLight.Red, result.TriggeredLight);
        Assert.Equal("Lesión activa", result.Reason);
    }

    [Fact]
    public void Evaluate_WithInjuryAndLittleSleep_PrioritizesRedOverYellow()
    {
        // Una lesión activa anula cualquier otra señal: el orden de prioridad es Rojo → Amarillo → Verde,
        // y el motivo unificado nombra todas las señales (KAN-560).
        var result = _engine.Evaluate("""{"hasActiveInjury": true, "sleepHours": 3, "effortRate": 9}""", "{}");

        Assert.Equal(TrafficLight.Red, result.TriggeredLight);
        Assert.Equal("Lesión activa, Esfuerzo alto, Muy poco sueño", result.Reason);
    }

    [Fact]
    public void Evaluate_WithActiveInjuryAndAlerts_ReturnsRedWithLowestResultValue()
    {
        // KAN-364: con lesión activa Y otras señales, el ResultValue debe ser el de Rojo (20),
        // no el de Amarillo (50): la prioridad del número sigue la misma que la del color.
        var result = _engine.Evaluate(
            new CheckInData { HasActiveInjury = true, SleepHours = 3, EffortRate = 9, IsSick = true },
            context: null);

        Assert.Equal(TrafficLight.Red, result.TriggeredLight);
        Assert.Equal(20, result.ResultValue);
    }

    [Fact]
    public void Evaluate_WithoutInjuryInTypedData_ReturnsGreen()
    {
        var result = _engine.Evaluate(
            new CheckInData { HasActiveInjury = false, SleepHours = 8, EffortRate = 4, IsSick = false },
            context: null);

        Assert.Equal(TrafficLight.Green, result.TriggeredLight);
        Assert.Equal("Estado óptimo", result.Reason);
    }

    // ---------- Amarillo: señales de alerta ----------

    [Fact]
    public void Evaluate_WithModerateSleepAndHighEffort_ReturnsYellowWithAllSignals()
    {
        var result = _engine.Evaluate("""{"sleepHours": 6, "effortRate": 9}""", "{}");

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Equal("Esfuerzo alto, Sueño moderado", result.Reason);
        Assert.Equal(50, result.ResultValue);
    }

    [Fact]
    public void Evaluate_WithoutCheckIn_ReturnsYellowForMissingData()
    {
        var result = _engine.Evaluate("", "{}");

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Equal("Sin check-in", result.Reason);
    }

    [Fact]
    public void Evaluate_WithNewPain_ReturnsYellow()
    {
        var result = _engine.Evaluate("""{"sleepHours": 8, "painZones": "Isquiotibial derecho"}""", "{}");

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Contains("Dolor nuevo", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    [InlineData(" [] ")]
    [InlineData("{}")]
    public void Evaluate_WithEmptyPainZones_DoesNotTriggerNewPain(string? painZones)
    {
        // KAN-364: "[]" es una lista vacía serializada por el front, no dolor nuevo.
        var result = _engine.Evaluate(
            new CheckInData { SleepHours = 8, EffortRate = 4, IsSick = false, PainZones = painZones },
            context: null);

        Assert.Equal(TrafficLight.Green, result.TriggeredLight);
        Assert.Equal("Estado óptimo", result.Reason);
    }

    [Theory]
    [InlineData("Isquiotibial derecho")]
    [InlineData("[\"rodilla\"]")]
    [InlineData("[\"rodilla\", \"tobillo\"]")]
    public void Evaluate_WithNonEmptyPainZones_ReturnsYellowWithNewPain(string painZones)
    {
        var result = _engine.Evaluate(
            new CheckInData { SleepHours = 8, EffortRate = 4, IsSick = false, PainZones = painZones },
            context: null);

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Contains("Dolor nuevo", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithEmptyPainZonesJsonArray_DoesNotTriggerNewPain()
    {
        // Camino JSON (KAN-363): el mismo criterio aplica cuando el dato llega como string JSON.
        var result = _engine.Evaluate("""{"sleepHours": 8, "effortRate": 4, "painZones": "[]"}""", "{}");

        Assert.Equal(TrafficLight.Green, result.TriggeredLight);
    }

    [Fact]
    public void Evaluate_WithSicknessInContext_ReturnsYellow()
    {
        // El contexto también es local: no se consulta a ningún servicio para saber si el atleta está enfermo.
        var result = _engine.Evaluate("""{"sleepHours": 8}""", """{"isSick": true}""");

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Contains("Enfermedad", result.Reason, StringComparison.Ordinal);
    }

    // ---------- Verde: estado óptimo ----------

    [Fact]
    public void Evaluate_InOptimalState_ReturnsGreen()
    {
        var result = _engine.Evaluate("""{"sleepHours": 8, "effortRate": 4, "isSick": false}""", "{}");

        Assert.Equal(TrafficLight.Green, result.TriggeredLight);
        Assert.Equal("Estado óptimo", result.Reason);
        Assert.Equal(90, result.ResultValue);
    }

    // ---------- Bordes y resiliencia a datos de entrada ----------

    [Theory]
    [InlineData(8.0, 7)] // en los umbrales exactos: ni sueño moderado (≥ 8) ni esfuerzo alto (no > 7)
    [InlineData(12.0, 4)]
    public void Evaluate_AtThresholdValues_ReturnsGreen(double sleepHours, int effortRate)
    {
        var result = _engine.Evaluate($$"""{"sleepHours": {{sleepHours}}, "effortRate": {{effortRate}}}""", "{}");

        Assert.Equal(TrafficLight.Green, result.TriggeredLight);
    }

    [Theory]
    [InlineData("esto no es json {{{")]
    [InlineData("\"es un string, no un objeto\"")]
    public void Evaluate_WithInvalidJson_DoesNotThrowAndFallsBackToYellow(string jsonInvalido)
    {
        // Un dato corrupto no puede tumbar el semáforo: se trata como ausencia de check-in.
        var result = _engine.Evaluate(jsonInvalido, "{}");

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Contains("Sin check-in", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithoutData_ReturnsYellowWithoutException()
    {
        // Sin check-in y sin contexto: el motor degrada a Amarillo, nunca lanza excepción.
        var result = _engine.Evaluate(evaluationDataJson: null!, contextDataJson: null!);

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
    }

    [Fact]
    public void Evaluate_Always_ReturnsReasonInSpanish()
    {
        var luces = new[]
        {
            _engine.Evaluate("""{"hasActiveInjury": true}""", "{}").Reason,
            _engine.Evaluate("""{"sleepHours": 3}""", "{}").Reason,
            _engine.Evaluate("""{"sleepHours": 8}""", "{}").Reason,
        };

        Assert.All(luces, motivo => Assert.False(string.IsNullOrWhiteSpace(motivo),
            "El semáforo siempre debe informar el motivo en español."));
    }

    // ---------- Amarillo: señales aisladas (KAN-367: exhaustividad de reglas) ----------

    [Fact]
    public void Evaluate_WithHighEffortOnly_ReturnsYellowWithSingleReason()
    {
        var result = _engine.Evaluate("""{"sleepHours": 8, "effortRate": 9}""", "{}");

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Equal("Esfuerzo alto", result.Reason);
        Assert.Equal(50, result.ResultValue);
    }

    [Fact]
    public void Evaluate_WithModerateSleepOnly_ReturnsYellowWithSingleReason()
    {
        // Sueño moderado (4 a < 8 h): amarillo con su motivo propio (KAN-560).
        var result = _engine.Evaluate("""{"sleepHours": 4, "effortRate": 4}""", "{}");

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Equal("Sueño moderado", result.Reason);
        Assert.Equal(50, result.ResultValue);
    }

    [Fact]
    public void Evaluate_WithVeryLittleSleepOnly_ReturnsRedWithSingleReason()
    {
        // Muy poco sueño (< 4 h): ahora es señal Roja (KAN-560).
        var result = _engine.Evaluate("""{"sleepHours": 3, "effortRate": 4}""", "{}");

        Assert.Equal(TrafficLight.Red, result.TriggeredLight);
        Assert.Equal("Muy poco sueño", result.Reason);
        Assert.Equal(20, result.ResultValue);
    }

    [Fact]
    public void Evaluate_WithSicknessInCheckInOnly_ReturnsYellow()
    {
        // La enfermedad marcada en el propio check-in (sin contexto) también enciende Amarillo.
        var result = _engine.Evaluate(
            new CheckInData { SleepHours = 8, EffortRate = 4, IsSick = true },
            context: null);

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Equal("Enfermedad", result.Reason);
        Assert.Equal(50, result.ResultValue);
    }

    [Fact]
    public void Evaluate_WithHasNewPainFlagOnly_ReturnsYellow()
    {
        // hasNewPain explícito dispara "Dolor nuevo" aunque painZones venga vacío.
        var result = _engine.Evaluate(
            new CheckInData { SleepHours = 8, EffortRate = 4, IsSick = false, HasNewPain = true },
            context: null);

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Equal("Dolor nuevo", result.Reason);
        Assert.Equal(50, result.ResultValue);
    }

    [Fact]
    public void Evaluate_WithAllAlertSignals_ReturnsYellowWithConcatenatedReasons()
    {
        // Todas las señales amarillas a la vez: el motivo concatena en el orden fijo del motor.
        var result = _engine.Evaluate(
            new CheckInData
            {
                SleepHours = 6,
                EffortRate = 9,
                PainZones = "cuadriceps_derecho:3",
                IsSick = true,
            },
            context: null);

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Equal("Esfuerzo alto, Sueño moderado, Dolor nuevo, Enfermedad", result.Reason);
        Assert.Equal(50, result.ResultValue);
    }

    // ---------- Rojo: prioridad máxima exhaustiva (KAN-367) ----------

    [Fact]
    public void Evaluate_WithEveryInjuryKeyAndEveryAlert_KeepsRedColorAndUnifiesEveryReason()
    {
        // Las tres claves de lesión (canónica y heredadas) + TODAS las alertas y contexto enfermo:
        // el color es Rojo con ResultValue 20 (nunca 50) y el motivo unificado nombra todas las
        // señales, en orden de severidad (KAN-560).
        var result = _engine.Evaluate(
            new CheckInData
            {
                HasActiveInjury = true,
                HasInjury = true,
                HasWound = true,
                SleepHours = 3,
                EffortRate = 9,
                PainZones = "espalda_baja:2",
                IsSick = true,
            },
            new ContextData { IsSick = true });

        Assert.Equal(TrafficLight.Red, result.TriggeredLight);
        Assert.Equal("Lesión activa, Esfuerzo alto, Muy poco sueño, Dolor nuevo, Enfermedad", result.Reason);
        Assert.Equal(20, result.ResultValue);
    }

    // ---------- Verde: banderas explícitas en false (KAN-367) ----------

    [Fact]
    public void Evaluate_WithEveryInjuryAndPainFlagExplicitlyFalse_ReturnsGreen()
    {
        // false explícito en todas las banderas: ninguna debe encender Rojo ni Amarillo.
        var result = _engine.Evaluate(
            new CheckInData
            {
                HasActiveInjury = false,
                HasInjury = false,
                HasWound = false,
                HasNewPain = false,
                IsSick = false,
                SleepHours = 8,
                EffortRate = 4,
                PainZones = "[]",
            },
            new ContextData { IsSick = false });

        Assert.Equal(TrafficLight.Green, result.TriggeredLight);
        Assert.Equal("Estado óptimo", result.Reason);
        Assert.Equal(90, result.ResultValue);
    }

    // ---------- Bordes: casing de JSON, contexto inválido y umbrales (KAN-367) ----------

    [Fact]
    public void Evaluate_WithUppercaseJsonKeys_IgnoresCasingAndReturnsYellow()
    {
        // PropertyNameCaseInsensitive: claves en mayúsculas siguen mapeando a los campos.
        var result = _engine.Evaluate("""{"SLEEPHOURS": 6, "EFFORTRATE": 9}""", "{}");

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Equal("Esfuerzo alto, Sueño moderado", result.Reason);
    }

    [Fact]
    public void Evaluate_WithInvalidContextJson_DoesNotThrowAndKeepsCheckInResult()
    {
        // Un contexto corrupto no invalida el check-in: se descarta el contexto y se evalúa el resto.
        var result = _engine.Evaluate("""{"sleepHours": 8, "effortRate": 4}""", "contexto {{{ roto");

        Assert.Equal(TrafficLight.Green, result.TriggeredLight);
        Assert.Equal("Estado óptimo", result.Reason);
    }

    [Theory]
    [InlineData(4, 8)] // justo pasando los umbrales: sueño moderado (≥ 4) y esfuerzo alto (> 7)
    [InlineData(7, 10)] // extremos de la escala sin cruzar el umbral rojo de sueño (< 4)
    public void Evaluate_JustBeyondThresholds_ReturnsYellow(int sleepHours, int effortRate)
    {
        var result = _engine.Evaluate($$"""{"sleepHours": {{sleepHours}}, "effortRate": {{effortRate}}}""", "{}");

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Equal(50, result.ResultValue);
    }

    // ---------- Reglas de sueño (KAN-560): rojo < 4 h, amarillo 4 a < 8 h, verde ≥ 8 h ----------

    [Theory]
    [InlineData(3.9, TrafficLight.Red, "Muy poco sueño")]
    [InlineData(4.0, TrafficLight.Yellow, "Sueño moderado")]
    [InlineData(7.9, TrafficLight.Yellow, "Sueño moderado")]
    [InlineData(8.0, TrafficLight.Green, "Estado óptimo")]
    public void Evaluate_AtSleepBoundaries_ReturnsExpectedLightAndReason(
        double sleepHours, TrafficLight expectedLight, string expectedReason)
    {
        var result = _engine.Evaluate(
            new CheckInData { SleepHours = sleepHours, EffortRate = 4, IsSick = false },
            context: null);

        Assert.Equal(expectedLight, result.TriggeredLight);
        Assert.Equal(expectedReason, result.Reason);
    }

    // ---------- Sueño prolongado: > 12 h en 3 de los últimos 7 días (KAN-560) ----------

    private static ContextData WindowWithProlongedDays(int prolongedDays, int sickProlongedDays = 0)
    {
        var window = new List<SleepWindowEntry>();
        for (int i = 0; i < prolongedDays; i++)
            window.Add(new SleepWindowEntry { SleepHours = 13, IsSick = false });
        for (int i = 0; i < sickProlongedDays; i++)
            window.Add(new SleepWindowEntry { SleepHours = 13, IsSick = true });
        while (window.Count < TrafficLightEngine.ProlongedSleepWindowDays)
            window.Add(new SleepWindowEntry { SleepHours = 7, IsSick = false });

        return new ContextData { SleepWindow = window };
    }

    [Fact]
    public void Evaluate_WithProlongedSleepInThreeOfSevenDays_ReturnsYellowWithFixedReason()
    {
        var result = _engine.Evaluate(
            new CheckInData { SleepHours = 8, EffortRate = 4, IsSick = false },
            WindowWithProlongedDays(prolongedDays: 3));

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Equal(TrafficLightEngine.ProlongedSleepReason, result.Reason);
        Assert.Equal(50, result.ResultValue);
    }

    [Fact]
    public void Evaluate_WithProlongedSleepInTwoOfSevenDays_DoesNotAlert()
    {
        var result = _engine.Evaluate(
            new CheckInData { SleepHours = 8, EffortRate = 4, IsSick = false },
            WindowWithProlongedDays(prolongedDays: 2));

        Assert.Equal(TrafficLight.Green, result.TriggeredLight);
        Assert.Equal("Estado óptimo", result.Reason);
    }

    [Fact]
    public void Evaluate_WithSickProlongedDays_DoesNotCountThem()
    {
        // 2 días prolongados sanos + 1 día prolongado enfermo: el día enfermo no cuenta → sin alerta.
        var result = _engine.Evaluate(
            new CheckInData { SleepHours = 8, EffortRate = 4, IsSick = false },
            WindowWithProlongedDays(prolongedDays: 2, sickProlongedDays: 1));

        Assert.Equal(TrafficLight.Green, result.TriggeredLight);
        Assert.Equal("Estado óptimo", result.Reason);
    }

    [Fact]
    public void Evaluate_WithProlongedSleepCombinedWithOtherSignal_UnifiesReasons()
    {
        // Si la alerta de sueño prolongado va junto a otra señal, se une con comas al resto (KAN-560).
        var result = _engine.Evaluate(
            new CheckInData { SleepHours = 8, EffortRate = 9, IsSick = false },
            WindowWithProlongedDays(prolongedDays: 3));

        Assert.Equal(TrafficLight.Yellow, result.TriggeredLight);
        Assert.Equal($"Esfuerzo alto, {TrafficLightEngine.ProlongedSleepReason}", result.Reason);
    }

    [Fact]
    public void Evaluate_WithoutSleepWindow_DoesNotEvaluateProlongedSleep()
    {
        // La vista "sin check-in" no trae ventana: la regla de prolongado no se evalúa.
        var result = _engine.Evaluate(
            new CheckInData { SleepHours = 8, EffortRate = 4, IsSick = false },
            new ContextData());

        Assert.Equal(TrafficLight.Green, result.TriggeredLight);
        Assert.Equal("Estado óptimo", result.Reason);
    }
}
