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
    [HttpPost("suggest-routine")]
    [HttpPost("/api/v1/asistente/sugerir-rutina")]
    public async Task<ActionResult<RoutineSuggestionResponseDto>> SuggestRoutine(
        [FromBody] RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await suggestRoutineUseCase.ExecuteAsync(request, cancellationToken);
        return Ok(result);
    }
}
