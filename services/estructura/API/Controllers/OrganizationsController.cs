using Application.DTOs.Organizations;
using Application.UseCases.Organizations.CreateOrganization;
using Application.UseCases.Organizations.DeleteOrganization;
using Application.UseCases.Organizations.GetOrganization;
using Application.UseCases.Organizations.ListOrganizations;
using Application.UseCases.Organizations.UpdateOrganization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/organizations")]
[Route("api/organizations")]
[Route("api/v1/organizaciones")]
[Authorize(Roles = "PreparadorFisico")]
public sealed class OrganizationsController(
    ListOrganizationsQueryHandler listHandler,
    GetOrganizationQueryHandler getHandler,
    CreateOrganizationCommandHandler createHandler,
    UpdateOrganizationCommandHandler updateHandler,
    DeleteOrganizationCommandHandler deleteHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrganizationDto>>> List(CancellationToken cancellationToken) =>
        Ok(await listHandler.HandleAsync(new ListOrganizationsQuery(), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrganizationDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await getHandler.HandleAsync(new GetOrganizationQuery(id), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<OrganizationDto>> Create(
        CreateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var organization = await createHandler.HandleAsync(
            new CreateOrganizationCommand(request.Name, request.Description),
            cancellationToken);

        return Created($"/api/v1/organizations/{organization.Id}", organization);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OrganizationDto>> Update(
        Guid id,
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await updateHandler.HandleAsync(
            new UpdateOrganizationCommand(id, request.Name, request.Description),
            cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await deleteHandler.HandleAsync(new DeleteOrganizationCommand(id), cancellationToken);
        return NoContent();
    }
}
