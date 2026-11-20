using Application.DTOs.Athletes;
using Application.DTOs.Categories;
using Application.UseCases.Athletes.AssignAthleteCategory;
using Application.UseCases.Athletes.CreateAthlete;
using Application.UseCases.Athletes.DeleteAthlete;
using Application.UseCases.Athletes.GetAthlete;
using Application.UseCases.Athletes.ListAthletes;
using Application.UseCases.Athletes.ListCategoriesByAthlete;
using Application.UseCases.Athletes.ListMedicalRecordAuditLogs;
using Application.UseCases.Athletes.ReadMedicalRecord;
using Application.UseCases.Athletes.RemoveAthleteCategory;
using Application.UseCases.Athletes.UpdateAthlete;
using Application.UseCases.Athletes.UpdateAthleteStatus;
using Application.UseCases.Athletes.UpdateAthleteEmail;
using Application.UseCases.Athletes.UpdateAthletePhone;
using Application.UseCases.Athletes.UpdateAthletePhysicalProfile;
using Application.UseCases.Athletes.UpdateHabitsPermission;
using Application.UseCases.Athletes.UpdateMedicalRecord;
using Application.UseCases.Athletes.UploadAthletePhoto;
using Application.UseCases.Athletes.GetAthletePhoto;
using Application.UseCases.Athletes.DeleteAthletePhoto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/athletes")]
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
    ListCategoriesByAthleteQueryHandler listCategoriesHandler,
    ReadMedicalRecordCommandHandler readMedicalRecordHandler,
    UpdateMedicalRecordCommandHandler updateMedicalRecordHandler,
    ListMedicalRecordAuditLogsQueryHandler listMedicalRecordAuditLogsHandler,
    UploadAthletePhotoCommandHandler uploadPhotoHandler,
    GetAthletePhotoQueryHandler getPhotoHandler,
    DeleteAthletePhotoCommandHandler deletePhotoHandler,
    UpdateAthleteStatusCommandHandler updateStatusHandler,
    UpdateAthleteEmailCommandHandler updateEmailHandler,
    UpdateAthletePhoneCommandHandler updatePhoneHandler,
    UpdateAthletePhysicalProfileCommandHandler updatePhysicalProfileHandler) : ControllerBase
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
                request.TelegramUsername),
            cancellationToken);

        return Created($"/api/v1/athletes/{athlete.Id}", athlete);
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
            new UpdateAthleteCommand(id, request.Phone, request.EmergencyContact, request.TelegramUsername),
            cancellationToken));

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<AthleteDto>> UpdateStatus(
        Guid id,
        UpdateAthleteStatusRequest request,
        CancellationToken cancellationToken) =>
        Ok(await updateStatusHandler.HandleAsync(
            new UpdateAthleteStatusCommand(id, request.Active),
            cancellationToken));

    [HttpPatch("{id:guid}/email")]
    public async Task<ActionResult<AthleteDto>> UpdateEmail(
        Guid id,
        UpdateAthleteEmailRequest request,
        CancellationToken cancellationToken) =>
        Ok(await updateEmailHandler.HandleAsync(
            new UpdateAthleteEmailCommand(id, request.Email),
            cancellationToken));

    [HttpPatch("{id:guid}/phone")]
    public async Task<ActionResult<AthleteDto>> UpdatePhone(
        Guid id,
        UpdateAthletePhoneRequest request,
        CancellationToken cancellationToken) =>
        Ok(await updatePhoneHandler.HandleAsync(
            new UpdateAthletePhoneCommand(id, request.Phone),
            cancellationToken));

    [HttpPatch("{id:guid}/physical-profile")]
    public async Task<ActionResult<AthleteDto>> UpdatePhysicalProfile(
        Guid id,
        UpdatePhysicalProfileRequest request,
        CancellationToken cancellationToken) =>
        Ok(await updatePhysicalProfileHandler.HandleAsync(
            new UpdateAthletePhysicalProfileCommand(id, request.WeightKg, request.AthleticGoal),
            cancellationToken));

    [HttpPost("{id:guid}/medical-record/access")]
    public async Task<ActionResult<MedicalRecordDto>> AccessMedicalRecord(
        Guid id,
        AccessMedicalRecordRequest request,
        CancellationToken cancellationToken) =>
        Ok(await readMedicalRecordHandler.HandleAsync(
            new ReadMedicalRecordCommand(id, request.Justification),
            cancellationToken));

    [HttpPut("{id:guid}/medical-record")]
    public async Task<ActionResult<MedicalRecordDto>> UpdateMedicalRecord(
        Guid id,
        UpdateMedicalRecordRequest request,
        CancellationToken cancellationToken) =>
        Ok(await updateMedicalRecordHandler.HandleAsync(
            new UpdateMedicalRecordCommand(
                id,
                request.Justification,
                request.MedicalHistory,
                request.Surgeries,
                request.Pathologies,
                request.HasPhysicalFitnessCertificate,
                request.PhysicalFitnessIssuedAt,
                request.PhysicalFitnessExpiresAt,
                request.PhysicalFitnessNotes),
            cancellationToken));

    [HttpGet("{id:guid}/medical-record/audit-logs")]
    public async Task<ActionResult<IReadOnlyList<MedicalRecordAuditLogDto>>> ListMedicalRecordAuditLogs(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await listMedicalRecordAuditLogsHandler.HandleAsync(
            new ListMedicalRecordAuditLogsQuery(id),
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
            new UploadAthletePhotoCommand(id, bytes, photo.ContentType, photo.Length),
            cancellationToken);

        return NoContent();
    }

    [HttpGet("{id:guid}/photo")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPhoto(Guid id, CancellationToken cancellationToken)
    {
        var photo = await getPhotoHandler.HandleAsync(new GetAthletePhotoQuery(id), cancellationToken);
        Response.Headers.CacheControl = "public, max-age=86400, stale-while-revalidate=3600";
        return File(photo.Data, photo.ContentType);
    }

    [HttpDelete("{id:guid}/photo")]
    public async Task<IActionResult> DeletePhoto(Guid id, CancellationToken cancellationToken)
    {
        await deletePhotoHandler.HandleAsync(new DeleteAthletePhotoCommand(id), cancellationToken);
        return NoContent();
    }
}
