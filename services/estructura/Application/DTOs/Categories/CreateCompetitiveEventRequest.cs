using System.Text.Json.Serialization;
using Domain.Entities;

namespace Application.DTOs.Categories;

public sealed record CreateCompetitiveEventRequest(
    DateOnly Date,
    TimeOnly? StartTime,
    string Opponent,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CompetitiveEventVenue Venue,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CompetitiveEventType Type,
    string? Notes);
