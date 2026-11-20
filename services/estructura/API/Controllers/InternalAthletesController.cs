using Application.DTOs.Athletes;
using Application.Interfaces.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Endpoints internos entre microservicios (Estructura -> Asistente / Seguimiento / Job de purga).
/// Exige rol 'Servicio' (#177 · KAN-472 · KAN-498).
/// </summary>
[Authorize(Roles = "Servicio")]
[ApiController]
[Route("api/v1/internal/athletes")]
public sealed class InternalAthletesController(
    IAthleteRepository athleteRepository,
    IEstructuraPurgeRepository estructuraPurgeRepository) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<InternalAthleteSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<InternalAthleteSummaryDto>>> GetActiveAthletes(CancellationToken cancellationToken)
    {
        var athletes = await athleteRepository.GetActiveAthletesInternalAsync(cancellationToken);
        return Ok(athletes);
    }

    /// <summary>
    /// Endpoint interno para purgar físicamente los datos locales de un atleta en Estructura.
    /// Nota: El pipeline automatizado de purga (DataPurgeWorker) invoca directamente IEstructuraPurgeRepository in-process;
    /// este endpoint HTTP queda expuesto para invocaciones administrativas o herramientas de soporte externas que requieran forzar la purga local.
    /// </summary>
    [HttpDelete("{athleteId:guid}/purge")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> PurgeAthlete(Guid athleteId, CancellationToken cancellationToken)
    {
        await estructuraPurgeRepository.PurgeAthleteLocalDataAsync(athleteId, cancellationToken);
        return NoContent();
    }
}
