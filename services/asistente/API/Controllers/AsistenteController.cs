using Application.DTOs.Ai;
using Application.UseCases.Ai.SuggestRoutine;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/asistente")]
public sealed class AsistenteController(SuggestRoutineUseCase suggestRoutineUseCase) : ControllerBase
{
    [HttpPost("sugerir-rutina")]
    public async Task<ActionResult<RoutineSuggestionResponseDto>> SuggestRoutine(
        [FromBody] RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await suggestRoutineUseCase.ExecuteAsync(request, cancellationToken);
        return Ok(result);
    }
}
