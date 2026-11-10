using Application.DTOs.Auth;
using Application.Interfaces.Services;
using Application.UseCases.Auth.ChangePassword;
using Application.UseCases.Auth.Login;
using Application.UseCases.Auth.Refresh;
using Application.UseCases.Auth.RegisterByInvitation;
using Application.UseCases.Auth.ResetAthletePassword;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Controlador para autenticación, registro, emisión de tokens y gestión de credenciales
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController(
    LoginUseCase loginUseCase,
    RefreshUseCase refreshTokenUseCase,
    RegisterByInvitationUseCase registerByInvitationUseCase,
    ChangePasswordUseCase changePasswordUseCase,
    ResetAthletePasswordUseCase resetAthletePasswordUseCase,
    ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// Inicia sesión con email y contraseña, emitiendo un token JWT firmado
    /// </summary>
    /// <param name="request">Credenciales del usuario.</param>
    /// <returns>HTTP 200 con el JWT o HTTP 401 si las credenciales son inválidas.</returns>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        var response = await loginUseCase.ExecuteAsync(request);
        return Ok(response);
    }

    /// <summary>
    /// Renueva el access token rotando el refresh token de 30 días para el dispositivo indicado
    /// </summary>
    /// <param name="request">Refresh token y deviceId del atleta.</param>
    /// <returns>HTTP 200 con el nuevo access token y refresh token rotado, o HTTP 401 si es inválido/expirado.</returns>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RefreshResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequestDto request)
    {
        var response = await refreshTokenUseCase.ExecuteAsync(request);
        return Ok(response);
    }

    /// <summary>
    /// Registra a un atleta mediante enlace o código de invitación (KAN-397)
    /// </summary>
    /// <param name="request">Datos del atleta y token de invitación.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>HTTP 200 con tokens de sesión o error correspondiente.</returns>
    [HttpPost("register-by-invitation")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    public async Task<IActionResult> RegisterByInvitation(
        [FromBody] RegisterByInvitationRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await registerByInvitationUseCase.ExecuteAsync(request, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Cambia la contraseña del usuario autenticado, valida complejidad y revoca sesiones activas (KAN-538).
    /// </summary>
    /// <param name="request">Contraseña actual y nueva contraseña.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>HTTP 204 si el cambio es exitoso, o HTTP 400 si la contraseña actual o nueva es inválida.</returns>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        await changePasswordUseCase.ExecuteAsync(userId.Value, request, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Resetea la contraseña de un atleta a cargo del Preparador Físico autenticado generando una contraseña temporal (KAN-539).
    /// </summary>
    /// <param name="athleteId">Identificador del atleta a resetear.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>HTTP 200 con la contraseña temporal generada y flag mustChangePassword.</returns>
    [HttpPost("athletes/{athleteId:guid}/reset-password")]
    [Authorize(Roles = "PreparadorFisico")]
    [ProducesResponseType(typeof(ResetAthletePasswordResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetAthletePassword(
        [FromRoute] Guid athleteId,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store, no-cache";
        Response.Headers.Pragma = "no-cache";

        var response = await resetAthletePasswordUseCase.ExecuteAsync(athleteId, cancellationToken);
        return Ok(response);
    }
}
