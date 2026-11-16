using System.Security.Claims;
using Application.DTOs.Auth;
using Application.Interfaces.Services;
using Application.UseCases.Auth.UpdatePhone;
using Application.UseCases.Auth.UpdateHabitsRecommendations;
using Application.UseCases.Auth.GetPfHabitsRecommendations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Application.DTOs.Users;
using Application.UseCases.Auth.GetPfContact;
using Application.UseCases.Users.UploadUserPhoto;
using Application.UseCases.Users.GetUserPhoto;
using Application.UseCases.Users.GetPfPhoto;
using Application.UseCases.Users.DeleteUserPhoto;

namespace API.Controllers;

/// <summary>
/// Controlador para la gestión del perfil del usuario autenticado
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/users")]
public class UsersController(
    UpdateWhatsAppPhoneUseCase updatePhoneUseCase,
    GetPfContactUseCase getPfContactUseCase,
    UpdateHabitsRecommendationsUseCase updateHabitsRecommendationsUseCase,
    GetPfHabitsRecommendationsUseCase getPfHabitsRecommendationsUseCase,
    ICurrentUser currentUser,
    UploadUserPhotoCommandHandler uploadPhotoHandler,
    GetUserPhotoQueryHandler getPhotoHandler,
    GetPfPhotoQueryHandler getPfPhotoHandler,
    DeleteUserPhotoCommandHandler deletePhotoHandler,
    Application.UseCases.Users.UpdateAthleteUserStatus.UpdateAthleteUserStatusUseCase updateAthleteStatusUseCase,
    Application.UseCases.Users.UpdateAthleteUserEmail.UpdateAthleteUserEmailUseCase updateAthleteEmailUseCase,
    Application.UseCases.Users.UpdateAthleteUserPhone.UpdateAthleteUserPhoneUseCase updateAthletePhoneUseCase) : ControllerBase
{
    /// <summary>
    /// Actualiza el número de WhatsApp del usuario autenticado.
    /// Valida y normaliza el número antes de persistir.
    /// </summary>
    /// <param name="request">Número de teléfono a guardar.</param>
    /// <returns>200 OK con el número normalizado, o 400 Bad Request si el formato es inválido.</returns>
    [HttpPut("me/phone")]
    [Authorize(Roles = "PreparadorFisico")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdatePhone([FromBody] UpdatePhoneRequestDto request)
    {
        // El id de la cuenta es el "sub" del token, nunca el PfId: en un atleta el PfId es el id de su PF.
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }
        var (success, normalizedPhone, errorMessage) = await updatePhoneUseCase.ExecuteAsync(userId.Value, request);
        if (!success)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Número de teléfono no válido.",
                Detail = errorMessage
            });
        }
        return Ok(new { phoneNumber = normalizedPhone });
    }

    /// <summary>
    /// Devuelve el WhatsApp del PF del atleta autenticado.
    /// </summary>
    /// <returns>200 OK con el número, o 404 si el PF todavía no lo cargó.</returns>
    [HttpGet("me/pf-contact")]
    [Authorize(Roles = "Atleta")]
    [ProducesResponseType(typeof(PfContactDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPfContact()
    {
        // El PF sale del token del atleta, así que no puede pedir el contacto de otro PF.
        var pfId = currentUser.PfId;
        if (pfId is null)
        {
            return Unauthorized();
        }

        var phoneNumber = await getPfContactUseCase.ExecuteAsync(pfId.Value);
        if (phoneNumber is null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Tu preparador todavía no cargó su WhatsApp."
            });
        }

        return Ok(new PfContactDto(phoneNumber));
    }

    /// <summary>
    /// Uploads or updates the authenticated user's profile photo in WebP format.
    /// </summary>
    [HttpPut("me/photo")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UploadMyPhoto(IFormFile photo, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        if (photo is null || photo.Length == 0)
        {
            throw new Domain.Exceptions.DomainException("El archivo de imagen no puede estar vacío.");
        }

        using var memoryStream = new MemoryStream();
        await photo.CopyToAsync(memoryStream, cancellationToken);
        var bytes = memoryStream.ToArray();

        await uploadPhotoHandler.HandleAsync(
            new UploadUserPhotoCommand(userId.Value, bytes, photo.ContentType, photo.Length),
            cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Returns the profile photo in WebP format for the user with the given id.
    /// </summary>
    [HttpGet("{id:guid}/photo")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserPhoto(Guid id, CancellationToken cancellationToken)
    {
        var photo = await getPhotoHandler.HandleAsync(new GetUserPhotoQuery(id), cancellationToken);
        Response.Headers.CacheControl = "public, max-age=86400, stale-while-revalidate=3600";
        return File(photo.Data, photo.ContentType);
    }

    /// <summary>
    /// Returns the profile photo in WebP format for the authenticated user.
    /// </summary>
    [HttpGet("me/photo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyPhoto(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var photo = await getPhotoHandler.HandleAsync(new GetUserPhotoQuery(userId.Value), cancellationToken);
        Response.Headers.CacheControl = "public, max-age=86400, stale-while-revalidate=3600";
        return File(photo.Data, photo.ContentType);
    }

    /// <summary>
    /// Returns the profile photo in WebP format for the physical trainer assigned to the authenticated athlete.
    /// </summary>
    [HttpGet("me/pf-photo")]
    [Authorize(Roles = "Atleta")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyPfPhoto(CancellationToken cancellationToken)
    {
        var pfId = currentUser.PfId;
        if (pfId is null)
        {
            return Unauthorized();
        }

        var photo = await getPfPhotoHandler.HandleAsync(new GetPfPhotoQuery(pfId.Value), cancellationToken);
        Response.Headers.CacheControl = "public, max-age=86400, stale-while-revalidate=3600";
        return File(photo.Data, photo.ContentType);
    }

    /// <summary>
    /// Deletes the authenticated user's profile photo.
    /// </summary>
    [HttpDelete("me/photo")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteMyPhoto(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        await deletePhotoHandler.HandleAsync(new DeleteUserPhotoCommand(userId.Value), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Actualiza el estado activo/inactivo de un atleta a cargo del Preparador Físico.
    /// Si se desactiva, revoca todas sus sesiones activas.
    /// </summary>
    [HttpPatch("athletes/{athleteId:guid}/status")]
    [Authorize(Roles = "PreparadorFisico")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAthleteStatus(
        [FromRoute] Guid athleteId,
        [FromBody] UpdateAthleteUserStatusRequestDto request,
        CancellationToken cancellationToken)
    {
        await updateAthleteStatusUseCase.ExecuteAsync(athleteId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Actualiza el correo electrónico de la cuenta de un atleta a cargo del Preparador Físico.
    /// </summary>
    [HttpPatch("athletes/{athleteId:guid}/email")]
    [Authorize(Roles = "PreparadorFisico")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAthleteEmail(
        [FromRoute] Guid athleteId,
        [FromBody] UpdateAthleteUserEmailRequestDto request,
        CancellationToken cancellationToken)
    {
        await updateAthleteEmailUseCase.ExecuteAsync(athleteId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Actualiza el número de teléfono de la cuenta de un atleta a cargo del Preparador Físico.
    /// </summary>
    [HttpPatch("athletes/{athleteId:guid}/phone")]
    [Authorize(Roles = "PreparadorFisico")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAthletePhone(
        [FromRoute] Guid athleteId,
        [FromBody] Application.DTOs.Users.UpdateAthleteUserPhoneRequestDto request,
        CancellationToken cancellationToken)
    {
        await updateAthletePhoneUseCase.ExecuteAsync(athleteId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Actualiza la preferencia del PF autenticado sobre el uso de recomendaciones de hábitos y alimentación.
    /// </summary>
    [HttpPut("me/habits-recommendations")]
    [Authorize(Roles = "PreparadorFisico")]
    [ProducesResponseType(typeof(PfHabitsRecommendationsStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateHabitsRecommendations([FromBody] UpdateHabitsRecommendationsRequestDto request)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var success = await updateHabitsRecommendationsUseCase.ExecuteAsync(userId.Value, request.Enabled);
        if (!success)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Usuario no encontrado."
            });
        }

        return Ok(new PfHabitsRecommendationsStatusDto(request.Enabled));
    }

    /// <summary>
    /// Devuelve la preferencia del PF autenticado sobre el uso de recomendaciones de hábitos y alimentación.
    /// </summary>
    [HttpGet("me/habits-recommendations")]
    [Authorize(Roles = "PreparadorFisico")]
    [ProducesResponseType(typeof(PfHabitsRecommendationsStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHabitsRecommendations()
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var enabled = await getPfHabitsRecommendationsUseCase.ExecuteAsync(userId.Value);
        if (enabled is null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Usuario no encontrado."
            });
        }

        return Ok(new PfHabitsRecommendationsStatusDto(enabled.Value));
    }

    /// <summary>
    /// Devuelve la preferencia de recomendaciones de hábitos y alimentación del PF del atleta autenticado.
    /// </summary>
    [HttpGet("me/pf-habits-recommendations")]
    [Authorize(Roles = "Atleta")]
    [ProducesResponseType(typeof(PfHabitsRecommendationsStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPfHabitsRecommendations()
    {
        var pfId = currentUser.PfId;
        if (pfId is null)
        {
            return Unauthorized();
        }

        var enabled = await getPfHabitsRecommendationsUseCase.ExecuteAsync(pfId.Value);
        if (enabled is null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Tu preparador no fue encontrado."
            });
        }

        return Ok(new PfHabitsRecommendationsStatusDto(enabled.Value));
    }
}
