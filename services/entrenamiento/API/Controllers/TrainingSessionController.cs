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

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TrainingSessionDto>> GetSession(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await _getSessionHandler.Handle(new GetTrainingSessionQuery(id), cancellationToken);
        if (session == null)
            return NotFound();

        return Ok(session);
    }

    [HttpPut("{id:guid}/notes")]
    public async Task<IActionResult> UpdateNotes(Guid id, [FromBody] UpdateCoachNotesRequest request, CancellationToken cancellationToken = default)
    {
        var command = new UpdateCoachNotesCommand(id, request.CoachNotes);
        await _updateCoachNotesHandler.Handle(command, cancellationToken);
        return NoContent();
    }
}