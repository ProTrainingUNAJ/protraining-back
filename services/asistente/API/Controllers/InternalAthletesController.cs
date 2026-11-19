using Application.DTOs.Internal;
using Application.Interfaces.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Endpoints internos entre microservicios (job de purga -> Asistente).
/// Exige rol 'Servicio' (#205 · KAN-500).
/// </summary>
[Authorize(Roles = "Servicio")]
[ApiController]
[Route("api/v1/internal/athletes")]
public sealed class InternalAthletesController(IAsistentePurgeRepository purgeRepository) : ControllerBase
{
    [HttpDelete("{athleteId:guid}/purge")]
    [ProducesResponseType(typeof(PurgedAthleteDataDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PurgedAthleteDataDto>> PurgeAthlete(Guid athleteId, CancellationToken cancellationToken)
    {
        var result = await purgeRepository.PurgeAthleteDataAsync(athleteId, cancellationToken);
        return Ok(result);
    }
}
