using System.Text.Json;
using System.Text.Json.Serialization;
using Domain.Entities;

namespace Domain.Services;

// Motor puro del semáforo: no depende de EF Core, de HTTP ni de la IA (KAN-363).
// Vive en Domain porque es lógica pura del negocio; se registra en Program.cs (KAN-364).
public sealed class TrafficLightEngine
{
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

        // Umbral de sueño: SleepHours < 5 horas
        const double SleepThreshold = 5.0;

        // Esfuerzo alto: EffortRate supera el umbral
        bool highEffort = checkInData?.EffortRate > EffortThreshold;

        // Poco sueño: SleepHours por debajo del umbral
        bool littleSleep = checkInData?.SleepHours < SleepThreshold;

        // Dolor nuevo: PainZones no vacío O HasNewPain explícito
        bool newPain = !string.IsNullOrWhiteSpace(checkInData?.PainZones) ||
                       checkInData?.HasNewPain == true;

        // Enfermedad: IsSick es true en el check-in o en el contexto
        bool isSick = checkInData?.IsSick == true ||
                      context?.IsSick == true;

        bool hasConcerningSigns = highEffort || littleSleep || newPain || isSick || noCheckIn;

        // Prioridad 3: estado óptimo (sin señales de alerta, sin lesión activa) → Verde
        bool isGoodState = !hasActiveInjury && !hasConcerningSigns;

        // Construir el motivo basado en la prioridad evaluada
        TrafficLight light;
        string reason;

        if (hasActiveInjury)
        {
            light = TrafficLight.Red;
            reason = "Lesión activa";
        }
        else if (hasConcerningSigns)
        {
            light = TrafficLight.Yellow;

            // Concatenar todas las señales de alerta encontradas
            var reasons = new List<string>();
            if (highEffort) reasons.Add("esfuerzo alto");
            if (littleSleep) reasons.Add("poco sueño");
            if (newPain) reasons.Add("dolor nuevo");
            if (isSick) reasons.Add("enfermedad");
            if (noCheckIn) reasons.Add("sin check-in");

            reason = $"Señales de alerta: {string.Join(", ", reasons)}";
        }
        else
        {
            light = TrafficLight.Green;
            reason = "Estado óptimo";
        }

        int resultValue = isGoodState ? 90 : hasConcerningSigns ? 50 : 20;

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
}

// Resultado de la evaluación con motivo obligatorio
public sealed record HeuristicEvaluationResult(
    int ResultValue,
    TrafficLight TriggeredLight,
    string Reason
);
