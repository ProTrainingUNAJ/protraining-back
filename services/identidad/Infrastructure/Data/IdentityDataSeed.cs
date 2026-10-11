using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Data;

/// <summary>
/// Carga usuarios y roles iniciales de prueba para el entorno de desarrollo.
/// </summary>
public static class IdentityDataSeed
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

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
        var testEmail = "pf@protraining.com";
        var existingUser = await userManager.FindByEmailAsync(testEmail);

        if (existingUser == null)
        {
            var pfUser = new Usuario
            {
                UserName = testEmail,
                Email = testEmail,
                NombreCompleto = "Prof. Alejandro Lopez",
                EmailConfirmed = true,
                Activo = true
            };

            var result = await userManager.CreateAsync(pfUser, "ProTraining2026!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(pfUser, "PreparadorFisico");
            }
        }
    }
}
