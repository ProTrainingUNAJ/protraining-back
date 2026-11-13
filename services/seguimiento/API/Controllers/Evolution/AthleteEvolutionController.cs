using Application.DTOs.Evolution;
using Application.UseCases.Evolution.GetAthleteEvolution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.Evolution;

[ApiController]
[Route("api/v1/athletes/{athleteId:guid}/evolution")]
[Authorize]
public sealed class AthleteEvolutionController(GetAthleteEvolutionQueryHandler queryHandler) : ControllerBase
{
    [HttpGet]
    [Tags("Evolution and Benchmark")]
    [ProducesResponseType(typeof(AthleteEvolutionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AthleteEvolutionResponseDto>> GetEvolution(
        [FromRoute] Guid athleteId,
        [FromQuery] int? months = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var result = await queryHandler.HandleAsync(
            new GetAthleteEvolutionQuery(athleteId, months, from, to),
            cancellationToken);

        return Ok(result);
    }
}
