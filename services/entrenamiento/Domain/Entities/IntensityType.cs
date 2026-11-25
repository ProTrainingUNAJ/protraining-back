using System.Text.Json.Serialization;

namespace Domain.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IntensityType
{
    None = 0,
    RPE = 1,
    RIR = 2
}
