using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Application.Common.PlanDrafts;

/// <summary>
/// Lee el día de la semana del borrador aunque la IA lo escriba en español ("Miércoles", "miercoles") o en inglés
/// ("Wednesday"). Los modelos chicos de Ollama a veces mezclan los dos idiomas en la misma respuesta, aunque el
/// prompt pida inglés, y eso invalidaba todo el borrador. Al escribir, siempre usa el nombre en inglés.
/// </summary>
public sealed class FlexibleDayOfWeekConverter : JsonConverter<DayOfWeek>
{
    private static readonly Dictionary<string, DayOfWeek> SpanishDays = new()
    {
        ["lunes"] = DayOfWeek.Monday,
        ["martes"] = DayOfWeek.Tuesday,
        ["miercoles"] = DayOfWeek.Wednesday,
        ["jueves"] = DayOfWeek.Thursday,
        ["viernes"] = DayOfWeek.Friday,
        ["sabado"] = DayOfWeek.Saturday,
        ["domingo"] = DayOfWeek.Sunday,
    };

    private static readonly Dictionary<string, DayOfWeek> DayPrefixes = new()
    {
        ["lu"] = DayOfWeek.Monday, ["mo"] = DayOfWeek.Monday,
        ["ma"] = DayOfWeek.Tuesday, ["tu"] = DayOfWeek.Tuesday,
        ["mi"] = DayOfWeek.Wednesday, ["we"] = DayOfWeek.Wednesday,
        ["ju"] = DayOfWeek.Thursday, ["th"] = DayOfWeek.Thursday,
        ["vi"] = DayOfWeek.Friday, ["fr"] = DayOfWeek.Friday,
        ["sa"] = DayOfWeek.Saturday,
        ["do"] = DayOfWeek.Sunday, ["su"] = DayOfWeek.Sunday,
    };

    public override DayOfWeek Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) && number is >= 0 and <= 6)
            return (DayOfWeek)number;

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString()?.Trim() ?? string.Empty;

            if (Enum.TryParse<DayOfWeek>(text, ignoreCase: true, out var english) && !int.TryParse(text, out _))
                return english;

            if (SpanishDays.TryGetValue(RemoveAccents(text.ToLowerInvariant()), out var spanish))
                return spanish;

            // El modelo a veces abrevia o escribe mal el día ("Mié", "Miérd"): las dos primeras letras alcanzan
            // para distinguir cualquier día, en español o en inglés
            if (DayPrefixes.TryGetValue(Prefix(text), out var byPrefix))
                return byPrefix;
        }

        throw new JsonException("El día de la semana del borrador no es válido.");
    }

    public override void Write(Utf8JsonWriter writer, DayOfWeek value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());

    // Las dos primeras letras, sin tildes, solo si hay al menos tres letras (para no aceptar cualquier sigla)
    private static string Prefix(string text)
    {
        var letters = new string(RemoveAccents(text.ToLowerInvariant()).Where(char.IsLetter).ToArray());
        return letters.Length >= 3 ? letters[..2] : string.Empty;
    }

    private static string RemoveAccents(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }
        return builder.ToString();
    }
}
