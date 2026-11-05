using Application.DTOs.Athletes;
using Application.DTOs.Categories;
using Application.UseCases.Categories.Invitations.AcceptCategoryInvitation;
using Application.UseCases.Categories.Invitations.ValidateCategoryInvitationToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/invitations")]
public sealed class InvitationsController(
    ValidateCategoryInvitationTokenQueryHandler validateHandler,
    AcceptCategoryInvitationCommandHandler acceptHandler) : ControllerBase
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

    [HttpPost("{token}/accept")]
    [Authorize(Roles = "Atleta")]
    public async Task<ActionResult<AthleteDto>> Accept(
        string token,
        [FromBody] AcceptCategoryInvitationRequest? request,
        CancellationToken cancellationToken)
    {
        var command = new AcceptCategoryInvitationCommand(
            token,
            request?.FirstName,
            request?.LastName,
            request?.Email,
            request?.Phone,
            request?.EmergencyContact);

        var athlete = await acceptHandler.HandleAsync(command, cancellationToken);
        return Ok(athlete);
    }
}