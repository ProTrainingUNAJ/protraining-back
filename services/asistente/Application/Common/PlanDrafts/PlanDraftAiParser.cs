namespace Application.Common.PlanDrafts;

using System.Text.Json;
using Application.Common.Ai;
using Application.DTOs.PlanDrafts;

public static class PlanDraftAiParser
{
    public static (bool IsValid, PlanDraftContentDto? Content, string? ErrorMessage, bool IsSyntaxError) Parse(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return (false, null, "El texto recibido del modelo de IA está vacío.", false);
        }

        var cleanedJson = AiRoutineParser.CleanMarkdownFences(rawResponse);

        try
        {
            var content = PlanDraftMapper.DeserializeContent(cleanedJson);
            if (content.Weeks == null || content.Weeks.Count == 0)
            {
                return (false, null, "El borrador generado no contiene ninguna semana ('weeks' está vacío).", false);
            }

            return (true, content, null, false);
        }
        catch (JsonException ex)
        {
            return (false, null, $"Sintaxis JSON inválida devuelta por la IA: {ex.Message}", true);
        }
        catch (Exception ex)
        {
            return (false, null, $"Error al deserializar el borrador de planificación: {ex.Message}", false);
        }
    }
}

