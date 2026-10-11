using Application.DTOs.Auth;
using Application.Interfaces.Services;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.Login;

/// <summary>
/// caso de uso que valida las credenciales del usuario y emite su token JWT
/// </summary>
public class LoginUseCase(
    UserManager<User> userManager,
    IJwtTokenGenerator jwtTokenGenerator)
{
    /// <summary>
    /// ejecuta la logica de autenticacion de un usuario
    /// </summary>
    /// <param name="request">Credenciales enviadas (Email y Password).</param>
    /// <returns>LoginResponse con el JWT si es válido, o null si las credenciales son incorrectas.</returns>
    public async Task<LoginResponseDto?> ExecuteAsync(LoginRequestDto request)
    {
        // 1. buscar al usuario por email
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null || !user.IsActive)
        {
            return null; // credenciales invalidas
        }

        // 2. verificar la contraseña con el hasher seguro PBKDF2
        var isPasswordValid = await userManager.CheckPasswordAsync(user, request.Password);
        if (!isPasswordValid)
        {
            return null; // Credenciales inválidas
        }

        // 3. obtener el rol del usuario (por defecto Atleta si no tiene)
        var roles = await userManager.GetRolesAsync(user);
        var primaryRole = roles.FirstOrDefault() ?? "Atleta";

        // 4. generar el JWT con claims unificados (sub, email, role, pf_id)
        var token = jwtTokenGenerator.GenerateToken(user, primaryRole);

        // 5. retornar la respuesta con 1 hora de expiracion (3600 segundos)
        return new LoginResponseDto(
            AccessToken: token,
            TokenType: "Bearer",
            ExpiresIn: 3600,
            User: new UserDto(
                user.Id,
                user.Email!,
                user.FullName,
                primaryRole,
                user.PfId ?? user.Id
            )
        );
    }
}
