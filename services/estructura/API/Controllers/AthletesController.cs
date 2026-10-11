using API.Requests;
using Application.DTOs;
using Application.UseCases.Athletes.CreateAthlete;
using Application.UseCases.Athletes.DeleteAthlete;
using Application.UseCases.Athletes.GetAthlete;
using Application.UseCases.Athletes.ListAthletes;
using Application.UseCases.Athletes.UpdateHabitsPermission;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/athletes")]
public sealed class AthletesController(
    ListAthletesQueryHandler listHandler,
    GetAthleteQueryHandler getHandler,
    CreateAthleteCommandHandler createHandler,
    UpdateHabitsPermissionCommandHandler updateHabitsPermissionHandler,
    DeleteAthleteCommandHandler deleteHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AthleteDto>>> List(CancellationToken cancellationToken) =>
        Ok(await listHandler.HandleAsync(new ListAthletesQuery(), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AthleteDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await getHandler.HandleAsync(new GetAthleteQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<AthleteDto>> Create(CreateAthleteRequest request, CancellationToken cancellationToken)
    {
        var athlete = await createHandler.HandleAsync(
            new CreateAthleteCommand(
                request.FirstName,
                request.LastName,
                request.Email,
                request.Phone,
                request.IsMinor,
                request.GuardianName,
                request.GuardianEmail,
                request.GuardianPhone,
                request.AllowsPhysicalTraining,
                request.AllowsHabitsRecommendations,
                request.EmergencyContact,
                request.MedicalConditions),
            cancellationToken);

        return Created($"/api/athletes/{athlete.Id}", athlete);
    }

    [HttpPatch("{id:guid}/habits-permission")]
    public async Task<ActionResult<AthleteDto>> UpdateHabitsPermission(
        Guid id,
        UpdateHabitsPermissionRequest request,
        CancellationToken cancellationToken)
    {
        var athlete = await updateHabitsPermissionHandler.HandleAsync(
            new UpdateHabitsPermissionCommand(id, request.AllowsHabitsRecommendations),
            cancellationToken);

        return Ok(athlete);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await deleteHandler.HandleAsync(new DeleteAthleteCommand(id), cancellationToken);
        return NoContent();
    }
}
