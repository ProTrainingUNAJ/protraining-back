using Application.Common.Security;
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
    // misma contraseña para todas las cuentas de prueba (solo Development)
    private const string DemoPassword = "ProTraining2026!";

    private const string PfEmail = "pf@protraining.com";
    private const string MainAthleteEmail = "atleta@protraining.com";

    // atletas de la Demo 1 (KAN-524): sus cuentas quedan con el PF de prueba, así el token
    // sale con su pf_id y Estructura los encuentra. El script scripts/demo-data.sh crea sus
    // perfiles en Estructura y los vincula a estas cuentas.
    private static readonly (string Email, string FullName)[] DemoAthletes =
    [
        (MainAthleteEmail, "Lucas Gómez"),
        ("martin@protraining.com", "Martín Pérez"),
        ("joaquin@protraining.com", "Joaquín Díaz"),
        ("facundo@protraining.com", "Facundo Sosa"),
        ("valentina@protraining.com", "Valentina Ruiz"),
        ("camila@protraining.com", "Camila Torres")
    ];

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
        var pfUser = await userManager.FindByEmailAsync(PfEmail);

        if (pfUser == null)
        {
            pfUser = new User
            {
                UserName = PfEmail,
                Email = PfEmail,
                FullName = "Prof. Alejandro Lopez",
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(pfUser, DemoPassword);
            if (!result.Succeeded)
            {
                return;
            }

            await userManager.AddToRoleAsync(pfUser, "PreparadorFisico");
        }

        // 3. crear los atletas de prueba con el PF de prueba; si ya existían sin PF
        // (bases creadas antes de la KAN-524), se les asigna
        foreach (var (email, fullName) in DemoAthletes)
        {
            var athleteUser = await userManager.FindByEmailAsync(email);

            if (athleteUser == null)
            {
                athleteUser = new User
                {
                    UserName = email,
                    Email = email,
                    FullName = fullName,
                    PfId = pfUser.Id,
                    EmailConfirmed = true,
                    IsActive = true
                };

                var result = await userManager.CreateAsync(athleteUser, DemoPassword);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(athleteUser, "Atleta");
                }
            }
            else if (athleteUser.PfId != pfUser.Id)
            {
                athleteUser.PfId = pfUser.Id;
                await userManager.UpdateAsync(athleteUser);
            }
        }

        // 4. sembrar refresh tokens de prueba para verificar KAN-306; desde la KAN-515
        // la tabla guarda el hash, así que se guarda el hash del valor conocido
        var mainAthlete = await userManager.FindByEmailAsync(MainAthleteEmail);
        if (mainAthlete != null)
        {
            var validToken = TokenHasher.Hash("valid-athlete-refresh-token-30-days");
            if (!await dbContext.RefreshTokens.AnyAsync(r => r.Token == validToken))
            {
                dbContext.RefreshTokens.Add(new RefreshToken
                {
                    UserId = mainAthlete.Id,
                    Token = validToken,
                    DeviceId = "device-atleta-01",
                    ExpiresAt = DateTime.UtcNow.AddDays(30),
                    CreatedAt = DateTime.UtcNow,
                    IsRevoked = false
                });
            }

            var revokedToken = TokenHasher.Hash("revoked-athlete-refresh-token");
            if (!await dbContext.RefreshTokens.AnyAsync(r => r.Token == revokedToken))
            {
                dbContext.RefreshTokens.Add(new RefreshToken
                {
                    UserId = mainAthlete.Id,
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
