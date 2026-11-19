using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Application.DTOs.TrainingPlans;

public sealed class RepsJsonConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt32(out var intVal))
                return intVal.ToString();
            if (reader.TryGetDecimal(out var decVal))
                return decVal.ToString(CultureInfo.InvariantCulture);
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
