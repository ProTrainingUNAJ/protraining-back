using Application.DTOs.Internal;
using Application.UseCases.Users.PurgeAthlete;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Endpoints internos entre microservicios (Estructura/Job de purga -> Identidad).
/// Exige rol 'Servicio' (#204 · KAN-499).
/// </summary>
[Authorize(Roles = "Servicio")]
[ApiController]
[Route("api/v1/internal/athletes")]
public sealed class InternalAthletesController(PurgeAthleteCommandHandler handler) : ControllerBase
{
    /// <summary>
    /// Purga física de los datos de un atleta/usuario en Identidad (cuentas de usuario, tokens, fotos).
    /// El parámetro accountId corresponde al AccountId de la cuenta en Identidad.
    /// </summary>
    [HttpDelete("{accountId:guid}/purge")]
    [ProducesResponseType(typeof(PurgedAthleteDataDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PurgedAthleteDataDto>> PurgeAthlete(Guid accountId, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new PurgeAthleteCommand(accountId), cancellationToken);
        return Ok(result);
    }
}
