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

    // ---- Regla de lesiones "En recuperación" (KAN-644 / KAN-590) ----
    // "En recuperación" quiere decir que el atleta está en vuelta progresiva, entrenando con ajustes;
    // no está parado (Ardern et al., 2016 Consensus statement on return to sport, Br J Sports Med 2016).
    // El umbral de 5/10 sale del modelo de monitoreo del dolor que se usa en rehabilitación: con dolor
    // de hasta 5/10 se sigue entrenando (Amarillo), y si pasa de 5 o empeora de un día a otro, se frena
    // (Rojo) (Silbernagel et al., Am J Sports Med 2007).
    public const int RecoveryPainThreshold = 5;
    public const string RecoveryInjuryWorsenedReason = "Lesión en recuperación con más dolor";

    // Marcador "Lesión" en PainZones ("zona:6") que se conserva para que Entrenamiento mantenga
    // las restricciones de ejercicios de la zona mientras la lesión no tenga el alta médica.
    private const string ClinicalInjuryPainLevel = "6";

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
        // El campo canónico es has_active_injury del check-in (solo lesiones Active o zona nueva con nivel 6);
        // hasInjury/hasWound se mantienen por compatibilidad con los JSON de evaluación de KAN-363.
        bool hasActiveInjury = checkInData?.HasActiveInjury == true ||
                               checkInData?.HasInjury == true ||
                               checkInData?.HasWound == true ||
                               HasUnregisteredLevel6Zone(checkInData?.PainZones, checkInData?.RegisteredInjuryZones, checkInData?.RecoveryInjuryMaxPainToday.HasValue == true);

        // Lesión "En recuperación" (InRecovery): señal amarilla si el dolor de hoy es 0–5 y no subió
        // respecto al check-in anterior; escala a roja si supera 5 o es más alto que en el check-in anterior.
        int? recoveryPainToday = checkInData?.RecoveryInjuryMaxPainToday;
        int? recoveryPainPrevious = checkInData?.RecoveryInjuryPreviousPain;
        bool hasRecoveryInjury = recoveryPainToday.HasValue;
        bool recoveryInjuryWorsened = hasRecoveryInjury &&
                                      (recoveryPainToday!.Value > RecoveryPainThreshold ||
                                       (recoveryPainPrevious.HasValue && recoveryPainToday.Value > recoveryPainPrevious.Value));
        bool recoveryInjuryStable = hasRecoveryInjury && !recoveryInjuryWorsened;

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

        // Dolor nuevo: PainZones con contenido real (niveles 1 a 5 o texto libre; el marcador ":6" representa
        // lesión y no dolor nuevo) O HasNewPain explícito (KAN-364).
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
        if (recoveryInjuryWorsened) reasons.Add(RecoveryInjuryWorsenedReason);
        else if (recoveryInjuryStable) reasons.Add($"Lesión en recuperación (dolor {recoveryPainToday!.Value}/10)");
        if (highEffort) reasons.Add("Esfuerzo alto");
        if (veryLowSleep) reasons.Add("Muy poco sueño");
        else if (moderateSleep) reasons.Add("Sueño moderado");
        if (prolongedSleep) reasons.Add(ProlongedSleepReason);
        if (newPain) reasons.Add("Dolor nuevo");
        if (isSick) reasons.Add("Enfermedad");
        if (noCheckIn) reasons.Add("Sin check-in");

        // El color más grave gana: Rojo por lesión activa, lesión en recuperación con más dolor
        // o muy poco sueño; si no, Amarillo por cualquier otra señal; si no, Verde.
        bool hasRedSign = hasActiveInjury || recoveryInjuryWorsened || veryLowSleep;
        bool hasYellowSign = recoveryInjuryStable || highEffort || moderateSleep || prolongedSleep ||
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

    // Detecta si en PainZones hay una zona marcada con ":6" (Lesión) que NO está en las lesiones
    // registradas de la ficha (ej. tiene rodilla en recuperación y hoy pinta hombro con nivel 6).
    // El marcador ":6" solo se ignora para las zonas de lesiones registradas.
    private static bool HasUnregisteredLevel6Zone(
        string? painZones,
        IReadOnlyCollection<string>? registeredInjuryZones,
        bool hasRecoveryInjury)
    {
        var normalized = painZones?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized is "[]" or "{}")
            return false;

        HashSet<string>? registeredSet = registeredInjuryZones is { Count: > 0 }
            ? registeredInjuryZones.Select(z => z.Trim()).Where(z => z.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var entry in normalized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (entry is "[]" or "{}")
                continue;

            var parts = entry.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[0].Length > 0 && parts[1] == ClinicalInjuryPainLevel)
            {
                if (registeredSet is not null)
                {
                    if (!registeredSet.Contains(parts[0]))
                        return true;
                }
                else if (!hasRecoveryInjury)
                {
                    // Sin lesiones registradas en la ficha: cualquier zona con ":6" es una lesión nueva reportada hoy.
                    return true;
                }
            }
        }

        return false;
    }

    // PainZones con contenido real (niveles 1..5 o texto libre): distingue "sin zonas" (null, vacío, "[]", "{}")
    // de una lista con ítems. Los tokens ":6" son marcadores de lesión (evaluados por HasUnregisteredLevel6Zone
    // o pertenecientes a lesiones de la ficha), no "Dolor nuevo" muscular.
    private static bool HasPainZones(string? painZones)
    {
        var normalized = painZones?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized is "[]" or "{}")
            return false;

        foreach (var entry in normalized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (entry is "[]" or "{}")
                continue;

            var parts = entry.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[1] == ClinicalInjuryPainLevel)
                continue;

            return true;
        }

        return false;
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

    // Lesión activa marcada en el check-in diario (columna has_active_injury, solo lesiones Active).
    public bool? HasActiveInjury { get; set; }

    // Dolor máximo de hoy y del check-in anterior para lesiones en estado InRecovery (KAN-644 / KAN-590).
    public int? RecoveryInjuryMaxPainToday { get; set; }
    public int? RecoveryInjuryPreviousPain { get; set; }

    // Zonas de lesiones registradas en la ficha (Active o InRecovery): el marcador ":6" en PainZones
    // solo se ignora para estas zonas registradas.
    public IReadOnlyCollection<string>? RegisteredInjuryZones { get; set; }

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
