using Application.DTOs.Recommendations;
using Application.UseCases.Recommendations.GenerateAthleteRecommendations;
using Application.UseCases.Recommendations.GenerateCategoryRecommendations;
using Application.UseCases.Recommendations.GetRecommendationById;
using Application.UseCases.Recommendations.GetRecommendations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace API.Controllers;

/// <summary>
/// Motor de recomendaciones de entrenamiento a partir del perfil frente al grupo y tendencias (#196 · KAN-491).
/// Todas las recomendaciones nacen pendientes de aprobación del Preparador Físico.
/// </summary>
[Authorize(Roles = "PreparadorFisico")]
[ApiController]
[Route("api/v1")]
public sealed class RecommendationsController(
    GenerateAthleteRecommendationsCommandHandler generateAthleteHandler,
    GenerateCategoryRecommendationsCommandHandler generateCategoryHandler,
    GetRecommendationsQueryHandler getRecommendationsHandler,
    GetRecommendationByIdQueryHandler getRecommendationByIdHandler) : ControllerBase
{
    /// <summary>
    /// Ejecuta el motor de recomendaciones para un atleta analizando su perfil frente al grupo y tendencias semanales.
    /// </summary>
    [HttpPost("athletes/{athleteId:guid}/recommendations")]
    [ProducesResponseType(typeof(RunAthleteRecommendationEngineResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(RunAthleteRecommendationEngineResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RunAthleteRecommendationEngineResponseDto>> GenerateForAthlete(
        Guid athleteId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] GenerateAthleteRecommendationsRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var result = await generateAthleteHandler.HandleAsync(
            new GenerateAthleteRecommendationsCommand(athleteId, request?.CategoryId, request?.Weeks),
            cancellationToken);

        if (result.GeneratedCount > 0)
        {
            return Created($"/api/v1/recommendations?athleteId={athleteId}", result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Ejecuta el motor de recomendaciones para todos los atletas activos de una categoría.
    /// </summary>
    [HttpPost("categories/{categoryId:guid}/recommendations")]
    [ProducesResponseType(typeof(RunCategoryRecommendationEngineResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(RunCategoryRecommendationEngineResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RunCategoryRecommendationEngineResponseDto>> GenerateForCategory(
        Guid categoryId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] GenerateCategoryRecommendationsRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var result = await generateCategoryHandler.HandleAsync(
            new GenerateCategoryRecommendationsCommand(categoryId, request?.Weeks),
            cancellationToken);

        if (result.GeneratedCount > 0)
        {
            return Created($"/api/v1/recommendations?categoryId={categoryId}", result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Lista las recomendaciones del preparador físico filtradas opcionalmente por atleta, categoría o estado.
    /// </summary>
    [HttpGet("recommendations")]
    [ProducesResponseType(typeof(IReadOnlyList<AthleteRecommendationResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AthleteRecommendationResponseDto>>> List(
        [FromQuery] Guid? athleteId = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await getRecommendationsHandler.HandleAsync(
            new GetRecommendationsQuery(athleteId, categoryId, status),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Obtiene el detalle de una recomendación por su identificador.
    /// </summary>
    [HttpGet("recommendations/{id:guid}")]
    [ProducesResponseType(typeof(AthleteRecommendationResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AthleteRecommendationResponseDto>> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await getRecommendationByIdHandler.HandleAsync(
            new GetRecommendationByIdQuery(id),
            cancellationToken);

        return Ok(result);
    }
}
