using API.Requests;
using Application.DTOs;
using Application.UseCases.Athletes.CreateAthlete;
using Application.UseCases.Athletes.DeleteAthlete;
using Application.UseCases.Athletes.GetAthlete;
using Application.UseCases.Athletes.ListAthletes;
using Application.UseCases.Athletes.UpdateAthleteCategory;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/athletes")]
public sealed class AthletesController(
    ListAthletesQueryHandler listHandler,
    GetAthleteQueryHandler getHandler,
    CreateAthleteCommandHandler createHandler,
    UpdateAthleteCategoryCommandHandler updateHandler,
    DeleteAthleteCommandHandler deleteHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AthleteDto>>> List(CancellationToken cancellationToken) =>
        Ok(await listHandler.HandleAsync(new ListAthletesQuery(), cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<AthleteDto>> Get(long id, CancellationToken cancellationToken) =>
        Ok(await getHandler.HandleAsync(new GetAthleteQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<AthleteDto>> Create(CreateAthleteRequest request, CancellationToken cancellationToken)
    {
        var athlete = await createHandler.HandleAsync(
            new CreateAthleteCommand(request.FullName, request.Category, request.Record1RmKg), cancellationToken);
        return Created($"/api/athletes/{athlete.Id}", athlete);
    }

    [HttpPatch("{id:long}")]
    public async Task<IActionResult> UpdateCategory(long id, UpdateAthleteCategoryRequest request, CancellationToken cancellationToken)
    {
        await updateHandler.HandleAsync(new UpdateAthleteCategoryCommand(id, request.Category), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await deleteHandler.HandleAsync(new DeleteAthleteCommand(id), cancellationToken);
        return NoContent();
    }
}
