using Application.DTOs.Auth;
using Application.UseCases.Auth.Login;
using Application.UseCases.Auth.RefreshToken;   
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// controlador para autenticacion y emision de tokens
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController(LoginUseCase loginUseCase, RefreshTokenUseCase refreshTokenUseCase) : ControllerBase
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

}
