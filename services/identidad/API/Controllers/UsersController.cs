using System.Security.Claims;
using Application.DTOs.Auth;
using Application.Interfaces.Services;
using Application.UseCases.Auth.UpdatePhone;
using Application.UseCases.Users.UpdateHabitsRecommendations;
using Application.UseCases.Users.GetPfHabitsRecommendations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Application.DTOs.Users;
using Application.UseCases.Auth.GetPfContact;
using Application.UseCases.Users.DeactivateAccount;
using Application.UseCases.Users.DeleteUserPhoto;
using Application.UseCases.Users.GetPfPhoto;
using Application.UseCases.Users.GetUserPhoto;
using Application.UseCases.Users.UpdateProfile;
using Application.UseCases.Users.UploadUserPhoto;

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
    Application.UseCases.Users.UpdateAthleteUserPhone.UpdateAthleteUserPhoneUseCase updateAthletePhoneUseCase,
    UpdateProfileUseCase updateProfileUseCase,
    Application.UseCases.Users.UpdateMyTelegram.UpdateMyTelegramUseCase updateMyTelegramUseCase,
    DeactivateAccountUseCase deactivateAccountUseCase,
    Microsoft.AspNetCore.Identity.UserManager<Domain.Entities.User> userManager) : ControllerBase
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

        var contact = await getPfContactUseCase.ExecuteAsync(pfId.Value);
        if (contact is null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Tu preparador todavía no cargó sus canales de contacto."
            });
        }

        return Ok(contact);
    }

    /// <summary>
    /// Uploads or updates the authenticated user's profile photo in WebP format.
    /// </summary>
    [HttpPut("me/photo")]
    [RequestSizeLimit(256_000)]
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

        if (photo.Length > Application.Common.WebpImageValidator.MaxSizeBytes)
        {
            throw new Domain.Exceptions.DomainException("La foto no puede superar los 200 KB.");
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
    /// Only the user themselves or the physical trainer assigned to the athlete can access it.
    /// </summary>
    [HttpGet("{id:guid}/photo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserPhoto(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = currentUser.UserId;
        if (currentUserId is null)
        {
            return Unauthorized();
        }

        // Solo se permite ver la propia foto o la de un atleta a cargo del PF autenticado.
        if (currentUserId.Value != id)
        {
            var targetUser = await userManager.FindByIdAsync(id.ToString());
            if (targetUser is null || !targetUser.PfId.HasValue || targetUser.PfId.Value != currentUserId.Value)
            {
                return NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Foto de perfil no encontrada."
                });
            }
        }

        var photo = await getPhotoHandler.HandleAsync(new GetUserPhotoQuery(id), cancellationToken);
        if (Response != null)
        {
            Response.Headers.CacheControl = "private, max-age=86400, stale-while-revalidate=3600";
        }
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
        if (Response != null)
        {
            Response.Headers.CacheControl = "private, max-age=86400, stale-while-revalidate=3600";
        }
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
        if (Response != null)
        {
            Response.Headers.CacheControl = "private, max-age=86400, stale-while-revalidate=3600";
        }
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

    /// <summary>
    /// Actualiza el perfil del Preparador Físico autenticado (Nombre y Teléfono) (KAN-601).
    /// </summary>
    /// <param name="request">Nombre completo y teléfono opcional.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>HTTP 200 con los datos actualizados del perfil.</returns>
    [HttpPut("me/profile")]
    [Authorize(Roles = "PreparadorFisico")]
    [ProducesResponseType(typeof(ProfileResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateProfileRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var response = await updateProfileUseCase.ExecuteAsync(userId.Value, request, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Actualiza exclusivamente el usuario de Telegram del usuario autenticado (PF o Atleta) (KAN-503).
    /// </summary>
    [HttpPut("me/telegram")]
    [Authorize(Roles = "PreparadorFisico,Atleta")]
    [ProducesResponseType(typeof(ProfileResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UpdateMyTelegram(
        [FromBody] UpdateTelegramRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var authHeader = Request.Headers.Authorization.ToString();
        var bearerToken = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader["Bearer ".Length..].Trim()
            : null;

        var response = await updateMyTelegramUseCase.ExecuteAsync(userId.Value, request, bearerToken, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Desactiva la cuenta del Preparador Físico autenticado tras validar su contraseña (KAN-601).
    /// Pone IsActive = false y revoca todas sus sesiones activas.
    /// </summary>
    /// <param name="request">Contraseña actual para confirmación de baja.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>HTTP 204 No Content si se desactiva con éxito o HTTP 400/401 si la contraseña es errónea.</returns>
    [HttpPost("me/deactivate")]
    [Authorize(Roles = "PreparadorFisico")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeactivateAccount(
        [FromBody] DeactivateAccountRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        await deactivateAccountUseCase.ExecuteAsync(userId.Value, request, cancellationToken);
        return NoContent();
    }
}
