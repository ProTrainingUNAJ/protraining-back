using Application.DTOs.Dashboard;
using Application.UseCases.Dashboard.GetCategoryDashboardSummary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.Categories;

[ApiController]
[Route("api/v1/categories")]
[Authorize(Roles = "PreparadorFisico")]
public sealed class CategoriesDashboardController(GetCategoryDashboardSummaryQueryHandler handler) : ControllerBase
{
    /// <summary>
    /// Resumen consolidado de presentismo, lesiones y carga de una categoría en un período (KAN-504).
    /// </summary>
    [HttpGet("{categoryId:guid}/summary")]
    [Tags("Dashboard")]
    [ProducesResponseType(typeof(CategoryDashboardSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CategoryDashboardSummaryDto>> GetSummary(
        [FromRoute] Guid categoryId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken = default) =>
        Ok(await handler.HandleAsync(
            new GetCategoryDashboardSummaryQuery(categoryId, from, to),
            cancellationToken));
}
