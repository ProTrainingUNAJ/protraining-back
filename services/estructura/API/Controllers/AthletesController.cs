using Application.DTOs.Athletes;
using Application.DTOs.Categories;
using Application.UseCases.Athletes.AssignAthleteCategory;
using Application.UseCases.Athletes.CreateAthlete;
using Application.UseCases.Athletes.DeleteAthlete;
using Application.UseCases.Athletes.GetAthlete;
using Application.UseCases.Athletes.ListAthletes;
using Application.UseCases.Athletes.ListCategoriesByAthlete;
using Application.UseCases.Athletes.RemoveAthleteCategory;
using Application.UseCases.Athletes.UpdateAthlete;
using Application.UseCases.Athletes.UpdateHabitsPermission;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/athletes")]
[Authorize(Roles = "PreparadorFisico")]
public sealed class AthletesController(
    ListAthletesQueryHandler listHandler,
    GetAthleteQueryHandler getHandler,
    CreateAthleteCommandHandler createHandler,
    UpdateAthleteCommandHandler updateHandler,
    UpdateHabitsPermissionCommandHandler updateHabitsPermissionHandler,
    DeleteAthleteCommandHandler deleteHandler,
    AssignAthleteCategoryCommandHandler assignCategoryHandler,
    RemoveAthleteCategoryCommandHandler removeCategoryHandler,
    ListCategoriesByAthleteQueryHandler listCategoriesHandler) : ControllerBase
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

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AthleteDto>> Update(
        Guid id,
        UpdateAthleteRequest request,
        CancellationToken cancellationToken) =>
        Ok(await updateHandler.HandleAsync(
            new UpdateAthleteCommand(id, request.Phone, request.EmergencyContact, request.MedicalConditions),
            cancellationToken));

    [HttpPost("{id:guid}/categories/{categoryId:guid}")]
    public async Task<IActionResult> AssignCategory(
        Guid id,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        var category = await assignCategoryHandler.HandleAsync(
            new AssignAthleteCategoryCommand(id, categoryId),
            cancellationToken);
        return Ok(category);
    }

    [HttpDelete("{id:guid}/categories/{categoryId:guid}")]
    public async Task<IActionResult> RemoveCategory(
        Guid id,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        await removeCategoryHandler.HandleAsync(
            new RemoveAthleteCategoryCommand(id, categoryId),
            cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> ListCategories(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await listCategoriesHandler.HandleAsync(new ListCategoriesByAthleteQuery(id), cancellationToken));
}
