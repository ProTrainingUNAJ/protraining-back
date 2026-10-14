using Application.DTOs.Categories;
using Application.UseCases.Athletes.ListMyCategories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// Datos del atleta logueado. No recibe ids: todo sale del token.
[ApiController]
[Route("api/v1/me")]
[Authorize(Roles = "Atleta")]
public sealed class MeController(ListMyCategoriesQueryHandler listMyCategoriesHandler) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> ListMyCategories(CancellationToken cancellationToken) =>
        Ok(await listMyCategoriesHandler.HandleAsync(new ListMyCategoriesQuery(), cancellationToken));
}
