using System.Security.Claims;
using Application.DTOs.Auth;
using Application.Interfaces.Services;
using Application.UseCases.Auth.UpdatePhone;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Controlador para la gestión del perfil del usuario autenticado
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/users")]
public class UsersController(UpdateWhatsAppPhoneUseCase updatePhoneUseCase, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// Actualiza el número de WhatsApp del usuario autenticado.
    /// Valida y normaliza el número antes de persistir.
    /// </summary>
    /// <param name="request">Número de teléfono a guardar.</param>
    /// <returns>200 OK con el número normalizado, o 400 Bad Request si el formato es inválido.</returns>
    [HttpPut("me/phone")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdatePhone([FromBody] UpdatePhoneRequestDto request)
    {
        var userId = currentUser.PfId;
        if (userId is null)
        {
            var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst("sub")?.Value;
            if (Guid.TryParse(sub, out var parsed))
            {
                userId = parsed;
            }
        }

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
}
