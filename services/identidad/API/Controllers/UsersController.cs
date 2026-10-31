using System.Security.Claims;
using Application.DTOs.Auth;
using Application.Interfaces.Services;
using Application.UseCases.Auth.UpdatePhone;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Application.DTOs.Users;
using Application.UseCases.Auth.GetPfContact;

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
    ICurrentUser currentUser) : ControllerBase
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
}
