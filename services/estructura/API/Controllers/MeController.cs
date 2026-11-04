using Application.DTOs.Athletes;
using Application.DTOs.Categories;
using Application.UseCases.Athletes.GetMe;
using Application.UseCases.Athletes.GetMyConsent;
using Application.UseCases.Athletes.ListMyCategories;
using Application.UseCases.Athletes.RegisterMyConsent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// Datos del atleta logueado. No recibe ids: todo sale del token.
[ApiController]
[Route("api/v1/me")]
[Authorize(Roles = "Atleta")]
public sealed class MeController(
    GetMeQueryHandler getMeHandler,
    ListMyCategoriesQueryHandler listMyCategoriesHandler,
    GetMyConsentQueryHandler getMyConsentHandler,
    RegisterMyConsentCommandHandler registerMyConsentHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeDto>> GetMe(CancellationToken cancellationToken) =>
        Ok(await getMeHandler.HandleAsync(new GetMeQuery(), cancellationToken));

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> ListMyCategories(CancellationToken cancellationToken) =>
        Ok(await listMyCategoriesHandler.HandleAsync(new ListMyCategoriesQuery(), cancellationToken));

    [HttpGet("consent")]
    public async Task<ActionResult<ConsentStatusDto>> GetMyConsent(CancellationToken cancellationToken) =>
        Ok(await getMyConsentHandler.HandleAsync(new GetMyConsentQuery(), cancellationToken));

    [HttpPost("consent")]
    public async Task<ActionResult<ConsentStatusDto>> RegisterMyConsent(
        [FromBody] RegisterAdultConsentRequest request,
        CancellationToken cancellationToken) =>
        Ok(await registerMyConsentHandler.HandleAsync(
            new RegisterMyConsentCommand(request.AllowsHabitsRecommendations, request.AllowsPhysicalTraining),
            cancellationToken));
}
