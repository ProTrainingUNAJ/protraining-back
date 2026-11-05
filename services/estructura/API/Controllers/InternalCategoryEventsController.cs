using System.Globalization;
using Application.DTOs.Categories;
using Application.Interfaces.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Endpoint interno para consulta de partidos de una categoría por servicios autorizados (rol 'Servicio') (#178 · KAN-473).
/// Utilizado por el Background Worker de sobrecarga en Asistente para correlación precompetitiva (D-1 / D-2).
/// </summary>
[ApiController]
[Route("api/v1/internal/categories/{categoryId:guid}/events")]
[Authorize(Roles = "Servicio")]
public sealed class InternalCategoryEventsController(
    ICompetitiveEventRepository eventRepository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CompetitiveEventDto>>> GetEvents(
        [FromRoute] Guid categoryId,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken)
    {
        DateOnly fromDate = DateOnly.MinValue;
        DateOnly toDate = DateOnly.MaxValue;

        if (!string.IsNullOrWhiteSpace(from) && DateOnly.TryParseExact(from.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedFrom))
        {
            fromDate = parsedFrom;
        }

        if (!string.IsNullOrWhiteSpace(to) && DateOnly.TryParseExact(to.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTo))
        {
            toDate = parsedTo;
        }

        var events = await eventRepository.GetMatchesByCategoryAndDateRangeInternalAsync(
            categoryId,
            fromDate,
            toDate,
            cancellationToken);

        var dtos = events.Select(e => new CompetitiveEventDto(
            e.Id,
            e.Date,
            e.StartTime,
            e.Opponent,
            e.Venue,
            e.Type,
            e.Notes)).ToList();

        return Ok(dtos);
    }
}

