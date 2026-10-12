using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Data;

/// <summary>
/// carga usuarios y roles iniciales de prueba para el entorno de desarrollo
/// </summary>
public static class IdentityDataSeed
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. crear roles si no existen
        string[] roles = ["PreparadorFisico", "Atleta", "Tutor"];
        foreach (var rol in roles)
        {
            if (!await roleManager.RoleExistsAsync(rol))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid> { Name = rol });
            }
        }

        // 2. crear un Preparador Fisico de prueba si no existe
        var pfEmail = "pf@protraining.com";
        var existingPf = await userManager.FindByEmailAsync(pfEmail);

        if (existingPf == null)
        {
            var pfUser = new User
            {
                UserName = pfEmail,
                Email = pfEmail,
                FullName = "Prof. Alejandro Lopez",
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(pfUser, "ProTraining2026!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(pfUser, "PreparadorFisico");
            }
        }

        // 3. crear un Atleta de prueba si no existe
        var athleteEmail = "atleta@protraining.com";
        var athleteUser = await userManager.FindByEmailAsync(athleteEmail);

        if (athleteUser == null)
        {
            athleteUser = new User
            {
                UserName = athleteEmail,
                Email = athleteEmail,
                FullName = "Lucas Gomez",
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(athleteUser, "ProTraining2026!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(athleteUser, "Atleta");
            }
        }

        // 4. sembrar refresh tokens de prueba para verificar KAN-306
        if (athleteUser != null)
        {
            var validToken = "valid-athlete-refresh-token-30-days";
            if (!await dbContext.RefreshTokens.AnyAsync(r => r.Token == validToken))
            {
                dbContext.RefreshTokens.Add(new RefreshToken
                {
                    UserId = athleteUser.Id,
                    Token = validToken,
                    DeviceId = "device-atleta-01",
                    ExpiresAt = DateTime.UtcNow.AddDays(30),
                    CreatedAt = DateTime.UtcNow,
                    IsRevoked = false
                });
            }

            var revokedToken = "revoked-athlete-refresh-token";
            if (!await dbContext.RefreshTokens.AnyAsync(r => r.Token == revokedToken))
            {
                dbContext.RefreshTokens.Add(new RefreshToken
                {
                    UserId = athleteUser.Id,
                    Token = revokedToken,
                    DeviceId = "device-atleta-01",
                    ExpiresAt = DateTime.UtcNow.AddDays(30),
                    CreatedAt = DateTime.UtcNow,
                    IsRevoked = true,
                    RevokedAt = DateTime.UtcNow
                });
            }

            await dbContext.SaveChangesAsync();
        }
    }
}
