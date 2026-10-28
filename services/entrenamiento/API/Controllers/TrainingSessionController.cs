using Application.DTOs.TrainingSessions;
using Application.UseCases.TrainingSessions.GetTrainingSession;
using Application.UseCases.TrainingSessions.UpdateCoachNotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/v1/training-sessions")]
[ApiController]
[Authorize]
public class TrainingSessionController : ControllerBase
{
    private readonly UpdateCoachNotesCommandHandler _updateCoachNotesHandler;
    private readonly GetTrainingSessionQueryHandler _getSessionHandler;

    public TrainingSessionController(
        UpdateCoachNotesCommandHandler updateCoachNotesHandler,
        GetTrainingSessionQueryHandler getSessionHandler)
    {
        _updateCoachNotesHandler = updateCoachNotesHandler;
        _getSessionHandler = getSessionHandler;
    }

    // Solo PF (KAN-450): una sesión puede ser de la planificación de otro atleta. El atleta ve
    // la suya, con las notas del PF, en GET /api/v1/me/today-routine.
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<ActionResult<TrainingSessionDto>> GetSession(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await _getSessionHandler.Handle(new GetTrainingSessionQuery(id), cancellationToken);
        if (session == null)
            return NotFound();

        return Ok(session);
    }

    [HttpPut("{id:guid}/notes")]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<IActionResult> UpdateNotes(Guid id, [FromBody] UpdateCoachNotesRequest request, CancellationToken cancellationToken = default)
    {
        var command = new UpdateCoachNotesCommand(id, request.CoachNotes);
        await _updateCoachNotesHandler.Handle(command, cancellationToken);
        return NoContent();
    }
}