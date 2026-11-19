using Application.DTOs.Auth;
using Application.Interfaces.Services;
using Application.UseCases.Auth.ChangePassword;
using Application.UseCases.Auth.ConfirmEmail;
using Application.UseCases.Auth.ForgotPassword;
using Application.UseCases.Auth.GoogleLogin;
using Application.UseCases.Auth.Login;
using Application.UseCases.Auth.Refresh;
using Application.UseCases.Auth.RegisterByInvitation;
using Application.UseCases.Auth.RegisterPf;
using Application.UseCases.Auth.ResendConfirmation;
using Application.UseCases.Auth.ResetAthletePassword;
using Application.UseCases.Auth.ResetPasswordWithToken;
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
    GoogleLoginUseCase googleLoginUseCase,
    LinkGoogleAccountUseCase linkGoogleAccountUseCase,
    UnlinkGoogleAccountUseCase unlinkGoogleAccountUseCase,
    GetGoogleLinkStatusUseCase getGoogleLinkStatusUseCase,
    RefreshUseCase refreshTokenUseCase,
    RegisterByInvitationUseCase registerByInvitationUseCase,
    RegisterPfUseCase registerPfUseCase,
    ConfirmEmailUseCase confirmEmailUseCase,
    ResendConfirmationUseCase resendConfirmationUseCase,
    ChangePasswordUseCase changePasswordUseCase,
    ResetAthletePasswordUseCase resetAthletePasswordUseCase,
    ForgotPasswordUseCase forgotPasswordUseCase,
    ResetPasswordWithTokenUseCase resetPasswordWithTokenUseCase,
    ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// Inicia sesión o vincula una cuenta existente mediante un ID Token de Google (#KAN-541).
    /// No permite auto-registro de usuarios ni creación de organizaciones/relaciones.
    /// </summary>
    /// <param name="request">ID Token de Google y deviceId del cliente.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>HTTP 200 con el JWT y datos del usuario, o HTTP 401 si no está habilitado/autorizado.</returns>
    [HttpPost("google")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GoogleLogin(
        [FromBody] GoogleLoginRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await googleLoginUseCase.ExecuteAsync(request, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Consulta si el usuario autenticado tiene una cuenta de Google vinculada (KAN-541).
    /// </summary>
    [HttpGet("google/link")]
    [Authorize]
    [ProducesResponseType(typeof(GoogleLinkStatusResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetGoogleLinkStatus()
    {
        if (currentUser.UserId is null)
        {
            return Unauthorized();
        }

        var response = await getGoogleLinkStatusUseCase.ExecuteAsync(currentUser.UserId.Value);
        return Ok(response);
    }

    /// <summary>
    /// Vincula explícitamente una cuenta de Google a la cuenta ProTraining autenticada,
    /// exigiendo reautenticación mediante contraseña actual (KAN-541).
    /// </summary>
    [HttpPost("google/link")]
    [Authorize]
    [ProducesResponseType(typeof(GoogleLinkStatusResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LinkGoogleAccount(
        [FromBody] LinkGoogleAccountRequestDto request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Unauthorized();
        }

        var response = await linkGoogleAccountUseCase.ExecuteAsync(
            currentUser.UserId.Value,
            request,
            cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Desvincula la cuenta de Google del usuario autenticado exigiendo contraseña actual (KAN-541).
    /// </summary>
    [HttpDelete("google/link")]
    [Authorize]
    [ProducesResponseType(typeof(GoogleLinkStatusResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UnlinkGoogleAccount([FromBody] UnlinkGoogleAccountRequestDto request)
    {
        if (currentUser.UserId is null)
        {
            return Unauthorized();
        }

        var response = await unlinkGoogleAccountUseCase.ExecuteAsync(currentUser.UserId.Value, request);
        return Ok(response);
    }

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

    /// <summary>
    /// Solicita un enlace de recuperación de contraseña por correo electrónico (#KAN-540).
    /// Siempre responde HTTP 202 Accepted para prevenir la enumeración de usuarios.
    /// </summary>
    /// <param name="request">Correo electrónico del usuario.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>HTTP 202 con mensaje genérico de confirmación.</returns>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ForgotPasswordResponseDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await forgotPasswordUseCase.ExecuteAsync(request, cancellationToken);
        return Accepted(response);
    }

    /// <summary>
    /// Restablece la contraseña de una cuenta utilizando el token recibido por correo (#KAN-540).
    /// </summary>
    /// <param name="request">Correo, token y nueva contraseña.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>HTTP 200 con confirmación de éxito o HTTP 400 si el token o la contraseña son inválidos.</returns>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ResetPasswordWithTokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordWithTokenRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await resetPasswordWithTokenUseCase.ExecuteAsync(request, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Registra un nuevo Preparador Físico de forma independiente y envía email de confirmación (KAN-601).
    /// </summary>
    /// <param name="request">Datos del PF (Nombre, Email, Contraseña, Teléfono opcional).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>HTTP 201 Created con mensaje de confirmación de email o error correspondiente.</returns>
    [HttpPost("register-pf")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RegisterPfResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterPf(
        [FromBody] RegisterPfRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await registerPfUseCase.ExecuteAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>
    /// Confirma el correo electrónico de la cuenta mediante el token recibido (KAN-601).
    /// </summary>
    /// <param name="request">Identificador de usuario y token de confirmación.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>HTTP 200 OK si la confirmación es exitosa o HTTP 400 si el token es inválido o expiró.</returns>
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ConfirmEmailResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmEmail(
        [FromBody] ConfirmEmailRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await confirmEmailUseCase.ExecuteAsync(request, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Reenvía el correo de confirmación de cuenta para activación de usuario (KAN-601).
    /// Responde HTTP 202 Accepted de forma genérica anti-enumeración.
    /// </summary>
    /// <param name="request">Correo electrónico de la cuenta.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>HTTP 202 Accepted con mensaje informativo.</returns>
    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ResendConfirmationResponseDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResendConfirmation(
        [FromBody] ResendConfirmationRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await resendConfirmationUseCase.ExecuteAsync(request, cancellationToken);
        return Accepted(response);
    }
}
