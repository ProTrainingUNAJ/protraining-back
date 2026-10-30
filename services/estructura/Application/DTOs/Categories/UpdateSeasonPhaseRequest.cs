using System.Text.Json.Serialization;
using Domain.Entities;

namespace Application.DTOs.Categories;

public sealed record UpdateSeasonPhaseRequest(
    string Name,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] SeasonPhaseType Type,
    DateOnly StartDate,
    DateOnly EndDate);
