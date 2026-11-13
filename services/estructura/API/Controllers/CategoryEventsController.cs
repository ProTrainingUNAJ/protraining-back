using Application.DTOs.Categories;
using Application.UseCases.Categories.Events.CreateCompetitiveEvent;
using Application.UseCases.Categories.Events.DeleteCompetitiveEvent;
using Application.UseCases.Categories.Events.ListCategoryEvents;
using Application.UseCases.Categories.Events.UpdateCompetitiveEvent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/categories/{categoryId:guid}/events")]
[Authorize]
public sealed class CategoryEventsController(
    ListCategoryEventsQueryHandler listHandler,
    CreateCompetitiveEventCommandHandler createHandler,
    UpdateCompetitiveEventCommandHandler updateHandler,
    DeleteCompetitiveEventCommandHandler deleteHandler) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "PreparadorFisico,Atleta")]
    public async Task<ActionResult<IReadOnlyList<CompetitiveEventDto>>> List(
        [FromRoute] Guid categoryId,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken) =>
        Ok(await listHandler.HandleAsync(new ListCategoryEventsQuery(categoryId, from, to), cancellationToken));

    [HttpPost]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<ActionResult<CompetitiveEventDto>> Create(
        [FromRoute] Guid categoryId,
        [FromBody] CreateCompetitiveEventRequest request,
        CancellationToken cancellationToken)
    {
        var created = await createHandler.HandleAsync(
            new CreateCompetitiveEventCommand(
                categoryId,
                request.Date,
                request.StartTime,
                request.Opponent,
                request.Venue,
                request.Type,
                request.Notes),
            cancellationToken);

        return Created($"/api/v1/categories/{categoryId}/events/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<ActionResult<CompetitiveEventDto>> Update(
        [FromRoute] Guid categoryId,
        [FromRoute] Guid id,
        [FromBody] UpdateCompetitiveEventRequest request,
        CancellationToken cancellationToken) =>
        Ok(await updateHandler.HandleAsync(
            new UpdateCompetitiveEventCommand(
                categoryId,
                id,
                request.Date,
                request.StartTime,
                request.Opponent,
                request.Venue,
                request.Type,
                request.Notes),
            cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid categoryId,
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        await deleteHandler.HandleAsync(new DeleteCompetitiveEventCommand(categoryId, id), cancellationToken);
        return NoContent();
    }
}
