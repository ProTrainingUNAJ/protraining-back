using System.Text.Json;
using System.Text.Json.Serialization;
using Domain.Entities;

namespace Domain.Services;

// Motor puro del semáforo: no depende de EF Core, de HTTP ni de la IA (KAN-363).
// Vive en Domain porque es lógica pura del negocio; se registra en Program.cs (KAN-364).
public sealed class TrafficLightEngine
{
    // ---- Reglas de sueño (KAN-560) — constantes nombradas para que el PF pueda ajustarlas ----
    // < 4 h → Rojo ("Muy poco sueño"); 4 a < 8 h → Amarillo ("Sueño moderado"); ≥ 8 h → sin alerta.
    public const double VeryLowSleepThreshold = 4.0;
    public const double LowSleepThreshold = 8.0;

    // Sueño prolongado: más de 12 h en al menos 3 de los últimos 7 días (los días "enfermo" no cuentan).
    public const double ProlongedSleepThreshold = 12.0;
    public const int ProlongedSleepDaysRequired = 3;
    public const int ProlongedSleepWindowDays = 7;

    // Texto fijo de la alerta de sueño prolongado (CA de KAN-560).
    public const string ProlongedSleepReason =
        "Durmió más de 12 horas en 3 de los últimos 7 días. Puede venir con cansancio acumulado o algo de salud: conviene charlarlo antes de entrenar.";

    // Opciones de serialización: ignora mayúsculas/minúsculas para claves JSON
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Entrada principal: el caso de uso entrega los datos ya tipados del check-in real (KAN-364).
    public HeuristicEvaluationResult Evaluate(CheckInData? checkInData, ContextData? context)
    {
        // Sin datos de check-in → el atleta no registró su estado del día
        bool noCheckIn = checkInData == null;

        // Prioridad 1: lesión activa detectada (máxima prioridad → Rojo)
        // El campo canónico es has_active_injury del check-in; hasInjury/hasWound se mantienen
        // por compatibilidad con los JSON de evaluación de KAN-363.
        bool hasActiveInjury = checkInData?.HasActiveInjury == true ||
                               checkInData?.HasInjury == true ||
                               checkInData?.HasWound == true;

        // Prioridad 2: señales de alerta (Amarillo)
        // Cada condición evalúa el VALOR REAL del campo, no solo la presencia de la clave

        // Umbral de esfuerzo: EffortRate > 7 (escala 1-10)
        const int EffortThreshold = 7;

        // Esfuerzo alto: EffortRate supera el umbral
        bool highEffort = checkInData?.EffortRate > EffortThreshold;

        // Sueño (KAN-560): muy poco sueño es señal Roja; el sueño moderado, Amarilla.
        bool veryLowSleep = checkInData?.SleepHours < VeryLowSleepThreshold;
        bool moderateSleep = checkInData?.SleepHours >= VeryLowSleepThreshold &&
                             checkInData?.SleepHours < LowSleepThreshold;

        // Dolor nuevo: PainZones con contenido real O HasNewPain explícito (KAN-364).
        // Un "[]" o "{}" (lista vacía serializada por el front) NO es dolor: se normaliza
        // para evitar el falso positivo de amarillo "dolor nuevo".
        bool newPain = HasPainZones(checkInData?.PainZones) ||
                       checkInData?.HasNewPain == true;

        // Enfermedad: IsSick es true en el check-in o en el contexto
        bool isSick = checkInData?.IsSick == true ||
                      context?.IsSick == true;

        // Sueño prolongado (KAN-560): la ventana de 7 días la entrega el caso de uso en el contexto.
        bool prolongedSleep = HasProlongedSleep(context);

        // Motivos unificados (KAN-560): una sola lista separada por comas y sin prefijo,
        // en orden de severidad, nombrando todas las señales encontradas.
        var reasons = new List<string>();
        if (hasActiveInjury) reasons.Add("Lesión activa");
        if (highEffort) reasons.Add("Esfuerzo alto");
        if (veryLowSleep) reasons.Add("Muy poco sueño");
        else if (moderateSleep) reasons.Add("Sueño moderado");
        if (prolongedSleep) reasons.Add(ProlongedSleepReason);
        if (newPain) reasons.Add("Dolor nuevo");
        if (isSick) reasons.Add("Enfermedad");
        if (noCheckIn) reasons.Add("Sin check-in");

        // El color más grave gana: Rojo por lesión activa o muy poco sueño; si no, Amarillo
        // por cualquier otra señal; si no, Verde.
        bool hasRedSign = hasActiveInjury || veryLowSleep;
        bool hasYellowSign = highEffort || moderateSleep || prolongedSleep ||
                             newPain || isSick || noCheckIn;

        TrafficLight light;
        string reason;

        if (hasRedSign)
        {
            light = TrafficLight.Red;
            reason = string.Join(", ", reasons);
        }
        else if (hasYellowSign)
        {
            light = TrafficLight.Yellow;
            reason = string.Join(", ", reasons);
        }
        else
        {
            light = TrafficLight.Green;
            reason = "Estado óptimo";
        }

        // ResultValue con la misma prioridad estricta que el color: Rojo manda sobre Amarillo (KAN-364).
        // Antes, con lesión activa Y otras señales, devolvía 50 (valor de Amarillo) contradiciendo el Rojo.
        int resultValue = hasRedSign ? 20 : hasYellowSign ? 50 : 90;

        return new HeuristicEvaluationResult(resultValue, light, reason);
    }

    // Entrada por JSON: la usan los tests y quien tenga JSON de evaluación a mano (KAN-363).
    public HeuristicEvaluationResult Evaluate(string evaluationDataJson, string contextDataJson)
    {
        // Deserializar el JSON de evaluación en un objeto tipado para evitar falsos positivos
        CheckInData? checkInData = null;
        if (!string.IsNullOrWhiteSpace(evaluationDataJson))
        {
            try
            {
                checkInData = JsonSerializer.Deserialize<CheckInData>(evaluationDataJson, JsonOptions);
            }
            catch (JsonException)
            {
                checkInData = null;
            }
        }

        // Deserializar el contexto si existe
        ContextData? context = null;
        if (!string.IsNullOrWhiteSpace(contextDataJson))
        {
            try
            {
                context = JsonSerializer.Deserialize<ContextData>(contextDataJson, JsonOptions);
            }
            catch (JsonException)
            {
                context = null;
            }
        }

        return Evaluate(checkInData, context);
    }

    // PainZones con contenido real: distingue "sin zonas" (null, vacío, "[]", "{}")
    // de una lista con ítems. Evita el falso positivo de "dolor nuevo" con "[]" (KAN-364).
    private static bool HasPainZones(string? painZones)
    {
        var normalized = painZones?.Trim();
        return !string.IsNullOrEmpty(normalized) && normalized is not ("[]" or "{}");
    }

    // Sueño prolongado (KAN-560): cuenta los días con más de 12 h dentro de la ventana que
    // entrega el caso de uso; los días "enfermo" no cuentan. Sin ventana en el contexto
    // (p. ej. la vista "sin check-in") la regla no se evalúa.
    private static bool HasProlongedSleep(ContextData? context)
    {
        var window = context?.SleepWindow;
        if (window == null || window.Count == 0)
            return false;

        int prolongedDays = window.Count(entry =>
            entry.SleepHours > ProlongedSleepThreshold && entry.IsSick != true);

        return prolongedDays >= ProlongedSleepDaysRequired;
    }
}

// Modelo tipado para los datos del check-in del atleta
public sealed class CheckInData
{
    public double? SleepHours { get; set; }
    public bool? IsSick { get; set; }
    public int? EffortRate { get; set; }
    public string? PainZones { get; set; }
    public bool? HasNewPain { get; set; }

    // Lesión activa marcada en el check-in diario (columna has_active_injury).
    public bool? HasActiveInjury { get; set; }

    // Claves heredadas de la evaluación JSON de KAN-363; se conservan por compatibilidad.
    public bool? HasInjury { get; set; }
    public bool? HasWound { get; set; }
}

// Modelo tipado para el contexto de evaluación
public sealed class ContextData
{
    public bool? IsSick { get; set; }

    // Ventana de check-ins (7 días anclados a la fecha del check-in) para la regla de
    // sueño prolongado (KAN-560). La arma el caso de uso con datos locales de la base.
    public List<SleepWindowEntry>? SleepWindow { get; set; }
}

// Un día dentro de la ventana de 7 días que evalúa el sueño prolongado.
public sealed class SleepWindowEntry
{
    public double? SleepHours { get; set; }
    public bool? IsSick { get; set; }
}

// Resultado de la evaluación con motivo obligatorio
public sealed record HeuristicEvaluationResult(
    int ResultValue,
    TrafficLight TriggeredLight,
    string Reason
);
