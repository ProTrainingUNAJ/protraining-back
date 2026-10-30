using Application.DTOs.CheckIns;
using Application.Interfaces.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Endpoint interno para el worker de recordatorios de wellness.
/// Exige rol 'Servicio' y devuelve en lote qué atletas ya hicieron check-in hoy.
/// </summary>
[Authorize(Roles = "Servicio")]
[ApiController]
[Route("api/v1/internal/check-ins")]
public class InternalCheckInsController(ICheckInRepository checkInRepository) : ControllerBase
{
    [HttpPost("done")]
    [ProducesResponseType(typeof(CheckInsDoneResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CheckInsDoneResponse>> GetCompletedCheckIns(
        [FromBody] CheckInsDoneRequest request,
        CancellationToken cancellationToken)
    {
        if (request.PfId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Parámetros inválidos",
                Detail = "El parámetro 'pfId' es obligatorio."
            });
        }

        var completedAthleteIds = await checkInRepository.GetCompletedCheckInAthleteIdsAsync(
            request.Date,
            request.PfId,
            request.AthleteIds,
            cancellationToken);

        return Ok(new CheckInsDoneResponse(completedAthleteIds));
    }
}