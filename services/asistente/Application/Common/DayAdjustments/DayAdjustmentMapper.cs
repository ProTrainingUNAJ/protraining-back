using System.Text;
using System.Text.Json;
using Application.DTOs.DayAdjustments;
using Domain.Entities;

namespace Application.Common.DayAdjustments;

public static class DayAdjustmentMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string SerializeItems(IReadOnlyList<DayAdjustmentItemDto> items) =>
        JsonSerializer.Serialize(items, JsonOptions);

    public static List<DayAdjustmentItemDto> DeserializeItems(string itemsJson)
    {
        if (string.IsNullOrWhiteSpace(itemsJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<DayAdjustmentItemDto>>(itemsJson, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static DayAdjustmentResponseDto ToResponseDto(DayAdjustment entity) =>
        new(
            Id: entity.Id,
            AthleteId: entity.AthleteId,
            Date: entity.Date,
            SessionId: entity.SessionId,
            Status: entity.Status.ToString(),
            Items: DeserializeItems(entity.Items),
            Justification: entity.Justification,
            Provider: entity.Provider,
            RejectionReason: entity.RejectionReason,
            CreatedAt: entity.CreatedAt
        );

    public static string BuildDeterministicJustification(IReadOnlyList<DayAdjustmentItemDto> items)
    {
        if (items.Count == 0)
            return "Se ajusta la sesión de hoy por molestia reportada en el check-in.";

        var clauses = new List<string>(items.Count);
        foreach (var item in items)
        {
            var zoneLabel = FormatZoneName(item.Zone);
            clauses.Add(
                $"Se cambia {item.OriginalExerciseName} por {item.SubstituteExerciseName} por molestia en {zoneLabel} (nivel {item.PainLevel}) reportada hoy");
        }

        return string.Join("; ", clauses) + ".";
    }

    public static string BuildJustificationPrompt(IReadOnlyList<DayAdjustmentItemDto> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Sos el asistente de un preparador físico en Argentina.");
        sb.AppendLine("Debés redactar ÚNICAMENTE una frase breve en español (1 o 2 oraciones) justificando el cambio de ejercicios para la sesión de hoy según las molestias reportadas en el check-in.");
        sb.AppendLine("REGLA ESTRICTA: No elijas otros ejercicios ni menciones nombres de personas. Usá exactamente los ejercicios y sustitutos listados abajo.");
        sb.AppendLine("Respondé en formato JSON estricto con la propiedad: {\"justification\": \"...\"}");
        sb.AppendLine("Cambios decididos por reglas:");

        foreach (var item in items)
        {
            var zoneLabel = FormatZoneName(item.Zone);
            sb.AppendLine($"- Original: {item.OriginalExerciseName} -> Sustituto: {item.SubstituteExerciseName} | Zona de dolor: {zoneLabel} (nivel {item.PainLevel}) | Motivo biomecánico: {item.Reason}");
        }

        return sb.ToString();
    }

    public static string? ExtractJustificationFromModelOutput(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return null;

        var trimmed = rawText.Trim();

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("justification", out var prop) && prop.ValueKind == JsonValueKind.String)
                {
                    var val = prop.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(val))
                        return val;
                }

                if (doc.RootElement.TryGetProperty("justificacion", out var propEs) && propEs.ValueKind == JsonValueKind.String)
                {
                    var val = propEs.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(val))
                        return val;
                }
            }
        }
        catch
        {
            // Si el modelo devolvió texto plano sin JSON válido, usar el texto plano si no parece roto
            if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
            {
                return trimmed.Trim('"');
            }
        }

        return null;
    }

    public static string FormatZoneName(string zone) =>
        (zone?.Trim().ToLowerInvariant()) switch
        {
            "cuadriceps" => "el cuádriceps",
            "rodilla" => "la rodilla",
            "espalda_baja" => "la espalda baja",
            "isquiotibiales" => "los isquiotibiales",
            "hombro" => "el hombro",
            "tobillo" => "el tobillo",
            "cadera" => "la cadera",
            "gemelos" => "los gemelos",
            "gluteos" => "los glúteos",
            "aductor" or "aductores" => "los aductores",
            var other when !string.IsNullOrWhiteSpace(other) => other.Replace('_', ' '),
            _ => "la zona afectada"
        };
}
