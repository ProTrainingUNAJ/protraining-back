using System.Security.Cryptography;
using Application.Common.Security;
using Application.DTOs.Auth;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Auth.RegisterByInvitation;

/// <summary>
/// Caso de uso para registrar a un atleta mediante token de invitación (KAN-397).
/// </summary>
public class RegisterByInvitationUseCase(
    UserManager<User> userManager,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenRepository refreshTokenRepository,
    IStructureClient structureClient)
{
    public async Task<LoginResponseDto> ExecuteAsync(
        RegisterByInvitationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            throw new DomainException("El correo electrónico es obligatorio.");

        if (string.IsNullOrWhiteSpace(request.Password))
            throw new DomainException("La contraseña es obligatoria.");

        if (string.IsNullOrWhiteSpace(request.FullName))
            throw new DomainException("El nombre completo es obligatorio.");

        if (string.IsNullOrWhiteSpace(request.Token))
            throw new InvitationNotFoundException("El token de invitación es obligatorio.");

        var cleanEmail = request.Email.Trim().ToLowerInvariant();

        // 1. Validar el token con Estructura (si expiró responde 410, si no existe responde 400)
        var invitation = await structureClient.ValidateInvitationTokenAsync(request.Token, cancellationToken);

        // 2. Verificar si el email ya existe en el sistema
        var existingUser = await userManager.FindByEmailAsync(cleanEmail);
        if (existingUser != null)
        {
            var existingRoles = await userManager.GetRolesAsync(existingUser);

            // Regla KAN-397: si ya existe con rol PF -> 403 Forbidden y no se crea nada
            if (existingRoles.Contains("PreparadorFisico"))
            {
                throw new PfRoleForbiddenException();
            }

            // Regla KAN-397: si ya existe como atleta -> 409 Conflict con mensaje "Ya tenés cuenta, iniciá sesión"
            throw new AthleteAlreadyExistsException();
        }

        // 3. Crear el nuevo usuario con rol Atleta
        var user = new User
        {
            UserName = cleanEmail,
            Email = cleanEmail,
            FullName = request.FullName.Trim(),
            EmailConfirmed = true,
            IsActive = true
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
            throw new DomainException($"No se pudo registrar el usuario: {errors}");
        }

        var roleResult = await userManager.AddToRoleAsync(user, "Atleta");
        if (!roleResult.Succeeded)
        {
            var errors = string.Join("; ", roleResult.Errors.Select(e => e.Description));
            throw new DomainException($"No se pudo asignar el rol Atleta: {errors}");
        }

        // 4. Generar tokens de sesión (JWT + Refresh Token de 30 días)
        var roles = await userManager.GetRolesAsync(user);
        var generatedToken = jwtTokenGenerator.GenerateToken(user, roles);

        var deviceId = string.IsNullOrWhiteSpace(request.DeviceId)
            ? Guid.NewGuid().ToString()
            : request.DeviceId.Trim();

        var refreshTokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = TokenHasher.Hash(refreshTokenValue),
            DeviceId = deviceId,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow
        };

        await refreshTokenRepository.AddAsync(refreshToken, cancellationToken);
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);

        // 5. Retornar los tokens de sesión para que el atleta quede autenticado
        return new LoginResponseDto(
            AccessToken: generatedToken.AccessToken,
            RefreshToken: refreshTokenValue,
            TokenType: "Bearer",
            ExpiresIn: generatedToken.ExpiresIn,
            User: new UserDto(
                user.Id,
                user.Email!,
                user.FullName,
                generatedToken.Role,
                user.PfId ?? user.Id,
                user.PhoneNumber
            )
        );
    }
}