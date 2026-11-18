using Application.DTOs.Trends;
using Application.UseCases.Trends.GetAthleteWeeklyTrends;
using Application.UseCases.Trends.GetCategoryWeeklyTrends;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.Trends;

[ApiController]
[Route("api/v1")]
[Authorize]
public sealed class TrendsController(
    GetAthleteWeeklyTrendsQueryHandler athleteTrendsHandler,
    GetCategoryWeeklyTrendsQueryHandler categoryTrendsHandler) : ControllerBase
{
    /// <summary>
    /// Tendencias semanales individuales de wellness y carga UA Foster de un atleta (KAN-489 · #194).
    /// Disponible para el PF responsable del atleta y para el propio atleta.
    /// </summary>
    [HttpGet("athletes/{athleteId:guid}/trends")]
    [Authorize(Roles = "PreparadorFisico,Atleta")]
    [Tags("Evolution and Benchmark")]
    [ProducesResponseType(typeof(AthleteWeeklyTrendsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AthleteWeeklyTrendsResponseDto>> GetAthleteTrends(
        [FromRoute] Guid athleteId,
        [FromQuery] int? weeks = 4,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var result = await athleteTrendsHandler.HandleAsync(
            new GetAthleteWeeklyTrendsQuery(athleteId, weeks, from, to),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Tendencias semanales grupales de wellness (promedio de promedios individuales) y carga total de una categoría (KAN-489 · #194).
    /// </summary>
    [HttpGet("categories/{categoryId:guid}/trends")]
    [Authorize(Roles = "PreparadorFisico")]
    [Tags("Evolution and Benchmark")]
    [ProducesResponseType(typeof(CategoryWeeklyTrendsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CategoryWeeklyTrendsResponseDto>> GetCategoryTrends(
        [FromRoute] Guid categoryId,
        [FromQuery] int? weeks = 4,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var result = await categoryTrendsHandler.HandleAsync(
            new GetCategoryWeeklyTrendsQuery(categoryId, weeks, from, to),
            cancellationToken);

        return Ok(result);
    }
}
