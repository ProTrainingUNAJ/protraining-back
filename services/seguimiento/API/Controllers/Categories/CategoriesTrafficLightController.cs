using Application.DTOs.TrafficLight;
using Application.UseCases.TrafficLight.GetCategoryTrafficLightToday;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.Categories;

// Semáforo del día por categoría para el PF (KAN-364): los atletas salen de Estructura con el
// token reenviado (que exige el rol PreparadorFisico) y los check-ins salen de esta base.
[ApiController]
[Route("api/v1/categories")]
[Authorize]
public sealed class CategoriesTrafficLightController(
    GetCategoryTrafficLightTodayQueryHandler handler) : ControllerBase
{
    // Pantalla del PF: estado tricolor de hoy por atleta (contrato: getCategoryTrafficLightToday).
    [HttpGet("{categoryId:guid}/traffic-light-today")]
    [ProducesResponseType(typeof(List<AthleteTrafficLightSummaryDto>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<List<AthleteTrafficLightSummaryDto>>> GetByCategoryToday(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        var result = await handler.HandleAsync(
            new GetCategoryTrafficLightTodayQuery(categoryId),
            cancellationToken);

        return Ok(result);
    }
}
