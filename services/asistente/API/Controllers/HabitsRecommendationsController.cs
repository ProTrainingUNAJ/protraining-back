using Application.DTOs.Recommendations;
using Application.UseCases.Recommendations.ApproveHabitsRecommendation;
using Application.UseCases.Recommendations.GetHabitsRecommendation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Recomendaciones de hábitos y nutrición para el Preparador Físico (contrato hábitos).
/// Genera recomendaciones seguras basadas en reglas y datos disponibles, y permite aprobarlas para el atleta.
/// </summary>
[Authorize(Roles = "PreparadorFisico")]
[ApiController]
[Route("api/v1")]
public sealed class HabitsRecommendationsController(
    GetHabitsRecommendationForAthleteQueryHandler getHabitsHandler,
    ApproveHabitsRecommendationCommandHandler approveHabitsHandler) : ControllerBase
{
    /// <summary>
    /// Obtiene/genera la recomendación de hábitos y proteína del atleta lista para validación del PF.
    /// </summary>
    [HttpGet("athletes/{athleteId:guid}/habits-recommendation")]
    [ProducesResponseType(typeof(HabitsRecommendationResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HabitsRecommendationResponseDto>> GetHabitsRecommendation(
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        var result = await getHabitsHandler.HandleAsync(
            new GetHabitsRecommendationForAthleteQuery(athleteId),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Aprueba una recomendación de hábitos y la publica para que sea visible al atleta.
    /// </summary>
    [HttpPatch("habits-recommendations/{recommendationId:guid}/approve")]
    [ProducesResponseType(typeof(ApproveHabitsRecommendationResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApproveHabitsRecommendationResponseDto>> ApproveHabitsRecommendation(
        Guid recommendationId,
        CancellationToken cancellationToken = default)
    {
        var result = await approveHabitsHandler.HandleAsync(
            new ApproveHabitsRecommendationCommand(recommendationId),
            cancellationToken);

        return Ok(result);
    }
}
