using Application.DTOs.Recommendations;
using Application.UseCases.Recommendations.GetMyRecommendations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Recomendaciones aprobadas por el PF visibles para el atleta autenticado (#197 · KAN-492).
/// No recibe athleteId: resuelve la identidad del atleta desde el token vía Estructura GET /api/v1/me.
/// </summary>
[Authorize(Roles = "Atleta")]
[ApiController]
[Route("api/v1/me/recommendations")]
public sealed class MeRecommendationsController(
    GetMyRecommendationsQueryHandler getMyRecommendationsHandler) : ControllerBase
{
    /// <summary>
    /// Lista las recomendaciones aprobadas por el preparador físico para el atleta autenticado.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MyRecommendationResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<MyRecommendationResponseDto>>> ListMine(
        CancellationToken cancellationToken = default)
    {
        var result = await getMyRecommendationsHandler.HandleAsync(
            new GetMyRecommendationsQuery(),
            cancellationToken);

        return Ok(result);
    }
}
