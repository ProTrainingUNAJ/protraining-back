using Application.DTOs.Categories;
using Application.UseCases.Categories.Invitations.ValidateCategoryInvitationToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/invitations")]
public sealed class InvitationsController(
    ValidateCategoryInvitationTokenQueryHandler validateHandler) : ControllerBase
{
    [HttpGet("{token}")]
    [HttpGet("{token}/validate")]
    [AllowAnonymous]
    public async Task<ActionResult<CategoryInvitationDto>> Validate(
        string token,
        CancellationToken cancellationToken) =>
        Ok(await validateHandler.HandleAsync(
            new ValidateCategoryInvitationTokenQuery(token),
            cancellationToken));
}