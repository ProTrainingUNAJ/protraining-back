using Application.DTOs.Auth;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Controlador interno para comunicación entre servicios (Gateway -> Identidad).
/// Exige rol 'Servicio' y firma criptográfica válida (#KAN-539 / #177).
/// </summary>
[Authorize(Roles = "Servicio")]
[ApiController]
[Route("api/v1/internal/auth")]
public class InternalAuthController(UserManager<User> userManager) : ControllerBase
{
    /// <summary>
    /// Valida la vigencia y estado de la sesión de un usuario contra PostgreSQL en tiempo real.
    /// </summary>
    [HttpPost("validate-session")]
    [ProducesResponseType(typeof(ValidateSessionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ValidateSessionResponseDto>> ValidateSession(
        [FromBody] ValidateSessionRequestDto request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            return Ok(new ValidateSessionResponseDto(false, false, "UserNotFound"));
        }

        if (!user.IsActive)
        {
            return Ok(new ValidateSessionResponseDto(false, false, "UserInactive"));
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return Ok(new ValidateSessionResponseDto(false, false, "UserLockedOut"));
        }

        if (string.IsNullOrWhiteSpace(request.SecurityStamp) ||
            !string.Equals(user.SecurityStamp, request.SecurityStamp, StringComparison.Ordinal))
        {
            return Ok(new ValidateSessionResponseDto(false, false, "SecurityStampMismatch"));
        }

        return Ok(new ValidateSessionResponseDto(
            IsValid: true,
            MustChangePassword: user.MustChangePassword));
    }
}
