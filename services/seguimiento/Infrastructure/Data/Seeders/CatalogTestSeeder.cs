using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Data.Seeders;

public static class CatalogTestSeeder
{
    public static async Task SeedAsync(AppDbContext context, ILogger logger)
    {
        // IgnoreQueryFilters: el filtro global ocultaría los registros ya sembrados en corridas previas.
        if (await context.CatalogTests.IgnoreQueryFilters().AnyAsync())
            return;

        var tests = new List<CatalogTest>
        {
            // Fuerza
            new("Sentadilla Baja con Barra", MotorCapacity.Strength, "kg", 3),
            new("Sentadilla 1RM", MotorCapacity.Strength, "kg", 3),
            new("Press Banca", MotorCapacity.Strength, "kg", 4),
            new("Dominadas", MotorCapacity.Strength, "repeticiones", 3),
            // Velocidad
            new("Carrera 30 m", MotorCapacity.Velocity, "s", 2),
            new("Sprint 60 m", MotorCapacity.Velocity, "s", 3),
            // Resistencia
            new("Test de Cooper (12 min)", MotorCapacity.Endurance, "m", 3),
            new("Test de Bronco", MotorCapacity.Endurance, "m", 3),
            new("Carrera continua 1 km", MotorCapacity.Endurance, "min", 2),
            // Potencia
            new("Salto Vertical CMJ", MotorCapacity.Power, "cm", 3),
            new("Salto Abalakov", MotorCapacity.Power, "cm", 3),
            // Flexibilidad
            new("Test de Sit and Reach", MotorCapacity.Flexibility, "cm", 1),
            // Agilidad
            new("Agility Illinois", MotorCapacity.Agility, "s", 4),
            new("Test de Cambios de Dirección", MotorCapacity.Agility, "s", 4),
            // Coordinación
            new("Test de Coordinación Bimanual", MotorCapacity.Coordination, "puntos", 2),
            new("Balance Unipodal con Ojos Cerrados", MotorCapacity.Coordination, "s", 2)
        };

        // Al no haber token en el arranque, SetBaseFields asigna PfId = Guid.Empty:
        // las 16 pruebas quedan como catálogo global visibles para todos los PF.
        await context.CatalogTests.AddRangeAsync(tests);
        await context.SaveChangesAsync();
        logger.LogInformation("Se sembraron {Count} pruebas del catálogo motor.", tests.Count);
    }
}
