using Application.DTOs.Categories;
using Application.UseCases.Categories.SeasonPhases.CreateSeasonPhase;
using Application.UseCases.Categories.SeasonPhases.DeleteSeasonPhase;
using Application.UseCases.Categories.SeasonPhases.ListCategorySeasonPhases;
using Application.UseCases.Categories.SeasonPhases.UpdateSeasonPhase;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/categories/{categoryId:guid}/season-phases")]
[Authorize]
public sealed class CategorySeasonPhasesController(
    ListCategorySeasonPhasesQueryHandler listHandler,
    CreateSeasonPhaseCommandHandler createHandler,
    UpdateSeasonPhaseCommandHandler updateHandler,
    DeleteSeasonPhaseCommandHandler deleteHandler) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "PreparadorFisico,Atleta")]
    public async Task<ActionResult<IReadOnlyList<SeasonPhaseDto>>> List(
        [FromRoute] Guid categoryId,
        CancellationToken cancellationToken) =>
        Ok(await listHandler.HandleAsync(new ListCategorySeasonPhasesQuery(categoryId), cancellationToken));

    [HttpPost]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<ActionResult<SeasonPhaseDto>> Create(
        [FromRoute] Guid categoryId,
        [FromBody] CreateSeasonPhaseRequest request,
        CancellationToken cancellationToken)
    {
        var created = await createHandler.HandleAsync(
            new CreateSeasonPhaseCommand(
                categoryId,
                request.Name,
                request.Type,
                request.StartDate,
                request.EndDate),
            cancellationToken);

        return Created($"/api/v1/categories/{categoryId}/season-phases/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<ActionResult<SeasonPhaseDto>> Update(
        [FromRoute] Guid categoryId,
        [FromRoute] Guid id,
        [FromBody] UpdateSeasonPhaseRequest request,
        CancellationToken cancellationToken) =>
        Ok(await updateHandler.HandleAsync(
            new UpdateSeasonPhaseCommand(
                categoryId,
                id,
                request.Name,
                request.Type,
                request.StartDate,
                request.EndDate),
            cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid categoryId,
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        await deleteHandler.HandleAsync(new DeleteSeasonPhaseCommand(categoryId, id), cancellationToken);
        return NoContent();
    }
}
