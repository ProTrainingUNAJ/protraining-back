using Application.DTOs.Averages;
using Application.UseCases.Categories.GetCategoryAverages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.Categories;

[ApiController]
[Route("api/v1/categories")]
[Authorize(Roles = "PreparadorFisico")]
public sealed class CategoriesAveragesController(
    GetCategoryAveragesQueryHandler handler) : ControllerBase
{
    /// <summary>
    /// Devuelve los promedios agregados de tests físicos y wellness semanal de los atletas de la categoría (KAN-447).
    /// </summary>
    [HttpGet("{categoryId:guid}/averages")]
    [ProducesResponseType(typeof(CategoryAveragesDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 503)]
    public async Task<ActionResult<CategoryAveragesDto>> GetCategoryAverages(
        [FromRoute] Guid categoryId,
        [FromQuery] string? weekStart,
        CancellationToken cancellationToken = default)
    {
        var result = await handler.HandleAsync(
            new GetCategoryAveragesQuery(categoryId, weekStart),
            cancellationToken);

        return Ok(result);
    }
}

