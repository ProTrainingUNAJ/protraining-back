using Application.DTOs.Organizations;
using Application.UseCases.Organizations.CreateOrganization;
using Application.UseCases.Organizations.DeleteOrganization;
using Application.UseCases.Organizations.GetOrganization;
using Application.UseCases.Organizations.ListOrganizations;
using Application.UseCases.Organizations.UpdateOrganization;
using Application.UseCases.Organizations.UploadOrganizationPhoto;
using Application.UseCases.Organizations.GetOrganizationPhoto;
using Application.UseCases.Organizations.DeleteOrganizationPhoto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/organizations")]
[Authorize(Roles = "PreparadorFisico")]
public sealed class OrganizationsController(
    ListOrganizationsQueryHandler listHandler,
    GetOrganizationQueryHandler getHandler,
    CreateOrganizationCommandHandler createHandler,
    UpdateOrganizationCommandHandler updateHandler,
    DeleteOrganizationCommandHandler deleteHandler,
    UploadOrganizationPhotoCommandHandler uploadPhotoHandler,
    GetOrganizationPhotoQueryHandler getPhotoHandler,
    DeleteOrganizationPhotoCommandHandler deletePhotoHandler) : ControllerBase
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

    [HttpPut("{id:guid}/photo")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadPhoto(Guid id, IFormFile photo, CancellationToken cancellationToken)
    {
        if (photo is null || photo.Length == 0)
        {
            throw new Domain.Exceptions.DomainException("El archivo de imagen no puede estar vacío.");
        }

        using var memoryStream = new MemoryStream();
        await photo.CopyToAsync(memoryStream, cancellationToken);
        var bytes = memoryStream.ToArray();

        await uploadPhotoHandler.HandleAsync(
            new UploadOrganizationPhotoCommand(id, bytes, photo.ContentType, photo.Length),
            cancellationToken);

        return NoContent();
    }

    [HttpGet("{id:guid}/photo")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPhoto(Guid id, CancellationToken cancellationToken)
    {
        var photo = await getPhotoHandler.HandleAsync(new GetOrganizationPhotoQuery(id), cancellationToken);
        Response.Headers.CacheControl = "public, max-age=86400, stale-while-revalidate=3600";
        return File(photo.Data, photo.ContentType);
    }

    [HttpDelete("{id:guid}/photo")]
    public async Task<IActionResult> DeletePhoto(Guid id, CancellationToken cancellationToken)
    {
        await deletePhotoHandler.HandleAsync(new DeleteOrganizationPhotoCommand(id), cancellationToken);
        return NoContent();
    }
}
