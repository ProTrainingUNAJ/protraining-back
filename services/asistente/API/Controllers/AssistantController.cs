using Application.DTOs.Ai;
using Application.UseCases.Ai.SuggestRoutine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Roles = "PreparadorFisico")]
[ApiController]
[Route("api/v1/assistant")]
public sealed class AssistantController(SuggestRoutineUseCase suggestRoutineUseCase) : ControllerBase
{
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
}
