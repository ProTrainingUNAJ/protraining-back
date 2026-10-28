using Application.UseCases.TrainingSessions.GetAthletesWithScheduledSession;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Endpoint interno para el worker de recordatorios de wellness.
/// Exige rol 'Servicio' y no puede ser accedido desde el exterior.
/// </summary>
[Authorize(Roles = "Servicio")]
[ApiController]
[Route("api/v1/internal/training-sessions")]
public class InternalTrainingSessionsController(GetAthletesWithScheduledSessionQueryHandler handler) : ControllerBase
{
    [HttpGet("athletes-with-session")]
    [ProducesResponseType(typeof(IReadOnlyList<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<Guid>>> GetAthletesWithScheduledSession(
        [FromQuery] DateOnly? date,
        [FromQuery] Guid? pfId,
        CancellationToken cancellationToken)
    {
        if (!date.HasValue || !pfId.HasValue || pfId.Value == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Parámetros inválidos",
                Detail = "Los parámetros 'date' y 'pfId' son obligatorios."
            });
        }

        var athleteIds = await handler.HandleAsync(
            new GetAthletesWithScheduledSessionQuery(date.Value, pfId.Value),
            cancellationToken);

        return Ok(athleteIds);
    }
}