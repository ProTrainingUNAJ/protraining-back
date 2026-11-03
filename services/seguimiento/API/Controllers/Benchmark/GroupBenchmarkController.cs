using Application.DTOs.Benchmark;
using Application.UseCases.Benchmark;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.Benchmark;

[ApiController]
[Route("api/v1/athletes/{athleteId:guid}/group-benchmark")]
[Authorize(Roles = "Atleta")]
public sealed class GroupBenchmarkController(
    GetAthleteGroupBenchmarkQueryHandler handler) : ControllerBase
{
    /// <summary>
    /// Retorna los números del atleta frente al promedio de su grupo (KAN-449).
    /// Si un atleta intenta consultar un ID ajeno, responde 403 Forbidden.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(GroupBenchmarkResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GroupBenchmarkResponseDto>> GetGroupBenchmark(
        [FromRoute] Guid athleteId,
        [FromQuery] Guid? categoryId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await handler.HandleAsync(
            new GetAthleteGroupBenchmarkQuery(athleteId, categoryId),
            cancellationToken);

        return Ok(result);
    }
}
