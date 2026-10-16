using Application.DTOs.Athletes;
using Application.DTOs.Categories;
using Application.UseCases.Categories.CreateCategory;
using Application.UseCases.Categories.DeleteCategory;
using Application.UseCases.Categories.GetCategory;
using Application.UseCases.Categories.ListAthletesByCategory;
using Application.UseCases.Categories.ListCategories;
using Application.UseCases.Categories.UpdateCategory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/categories")]
[Route("api/categories")]
[Route("api/v1/categorias")]
[Route("api/categorias")]
[Authorize(Roles = "PreparadorFisico")]
public sealed class CategoriesController(
    ListCategoriesQueryHandler listHandler,
    GetCategoryQueryHandler getHandler,
    CreateCategoryCommandHandler createHandler,
    UpdateCategoryCommandHandler updateHandler,
    DeleteCategoryCommandHandler deleteHandler,
    ListAthletesByCategoryQueryHandler listAthletesHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> List(
        [FromQuery] Guid organizationId,
        CancellationToken cancellationToken) =>
        Ok(await listHandler.HandleAsync(new ListCategoriesQuery(organizationId), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await getHandler.HandleAsync(new GetCategoryQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create(
        [FromQuery] Guid organizationId,
        CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await createHandler.HandleAsync(
            new CreateCategoryCommand(organizationId, request.Name, request.Description),
            cancellationToken);

        return Created($"/api/v1/categories/{category.Id}", category);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CategoryDto>> Update(
        Guid id,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken) =>
        Ok(await updateHandler.HandleAsync(
            new UpdateCategoryCommand(id, request.Name, request.Description),
            cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await deleteHandler.HandleAsync(new DeleteCategoryCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/athletes")]
    public async Task<ActionResult<IReadOnlyList<AthleteDto>>> ListAthletes(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await listAthletesHandler.HandleAsync(new ListAthletesByCategoryQuery(id), cancellationToken));
}
