using Application.DTOs.Ai;
using Application.DTOs.PfDecisions;
using Application.UseCases.Ai.SuggestRoutine;
using Application.UseCases.PfDecisions.GetAthleteDecisions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Roles = "PreparadorFisico")]
[ApiController]
[Route("api/v1/assistant")]
public sealed class AssistantController(
    SuggestRoutineUseCase suggestRoutineUseCase,
    GetAthleteDecisionsQueryHandler getAthleteDecisionsHandler) : ControllerBase
{
    /// <summary>
    /// Constructor de compatibilidad para pruebas enfocadas exclusivamente en la sugerencia de rutinas.
    /// </summary>
    public AssistantController(SuggestRoutineUseCase suggestRoutineUseCase)
        : this(suggestRoutineUseCase, null!)
    {
    }

    /// <summary>
    /// Genera una propuesta estructurada de rutina consultando directamente al módulo de IA (#086 · KAN-381).
    /// </summary>
    [HttpPost("suggest-routine")]
    [ProducesResponseType(typeof(RoutineSuggestionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RoutineSuggestionResponseDto>> SuggestRoutine(
        [FromBody] RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await suggestRoutineUseCase.ExecuteAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Consulta las decisiones históricas del PF sobre propuestas del asistente (#128 · KAN-423).
    /// Devuelve como máximo las últimas 50 decisiones para el atleta especificado, con filtro opcional por zona.
    /// Responde 404 si el atleta no existe o no pertenece al preparador físico autenticado.
    /// </summary>
    [HttpGet("athletes/{athleteId:guid}/decisions")]
    [ProducesResponseType(typeof(IReadOnlyList<PfDecisionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<PfDecisionResponseDto>>> GetAthleteDecisions(
        Guid athleteId,
        [FromQuery] string? zone,
        CancellationToken cancellationToken)
    {
        var result = await getAthleteDecisionsHandler.HandleAsync(
            new GetAthleteDecisionsQuery(athleteId, zone),
            cancellationToken);

        return Ok(result);
    }
}
