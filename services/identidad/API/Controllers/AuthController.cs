using Application.DTOs.Auth;
using Application.UseCases.Auth.Login;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// controlador para autenticacion y emision de tokens
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController(LoginUseCase loginUseCase) : ControllerBase
{
    /// <summary>
    /// inicia sesion con email y contraseña, emitiendo un token JWT firmado
    /// </summary>
    /// <param name="request">Credenciales del usuario.</param>
    /// <returns>HTTP 200 con el JWT o HTTP 401 si las credenciales son invalidas.</returns>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        var response = await loginUseCase.ExecuteAsync(request);

        if (response == null)
        {
            // HTTP 401 con mensaje genérico
            return Unauthorized(new { message = "Email o contraseña incorrectos." });
        }

        // TTP 200 con JWT y expiración
        return Ok(response);
    }
}
