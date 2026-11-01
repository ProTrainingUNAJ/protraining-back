using Application.DTOs.Auth;
using Application.UseCases.Auth.Login;
using Application.UseCases.Auth.Refresh;
using Application.UseCases.Auth.RegisterByInvitation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// controlador para autenticacion, registro y emision de tokens
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/v1/auth")]
public class AuthController(
    LoginUseCase loginUseCase,
    RefreshUseCase refreshTokenUseCase,
    RegisterByInvitationUseCase registerByInvitationUseCase) : ControllerBase
{
    /// <summary>
    /// inicia sesion con email y contraseña, emitiendo un token JWT firmado
    /// </summary>
    /// <param name="request">Credenciales del usuario.</param>
    /// <returns>HTTP 200 con el JWT o HTTP 401 si las credenciales son invalidas.</returns>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        var response = await loginUseCase.ExecuteAsync(request);
        return Ok(response);
    }

    /// <summary>
    /// renueva el access token rotando el refresh token de 30 dias para el dispositivo indicado
    /// </summary>
    /// <param name="request">Refresh token y deviceId del atleta.</param>
    /// <returns>HTTP 200 con el nuevo access token y refresh token rotado, o HTTP 401 si es invalido/expirado.</returns>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(RefreshResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequestDto request)
    {
        var response = await refreshTokenUseCase.ExecuteAsync(request);
        return Ok(response);
    }

    /// <summary>
    /// registra a un atleta mediante enlace o código de invitación (KAN-397)
    /// </summary>
    /// <param name="request">Datos del atleta y token de invitación.</param>
    /// <returns>HTTP 200 con tokens de sesión o error correspondiente.</returns>
    [HttpPost("register-by-invitation")]
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
}