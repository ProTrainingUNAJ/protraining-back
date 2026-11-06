using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Data.Seeders;

public static class CatalogTestSeeder
{
    // KAN-426: tests de fuerza en kg que generan récord 1RM en Entrenamiento, con el nombre
    // exacto del ejercicio homónimo en su catálogo (los dos catálogos no comparten ids ni
    // nombres). Solo "Sentadilla 1RM" queda mapeada por ahora: no se adivinan equivalencias
    // para el resto (p. ej. "Press Banca") hasta confirmar el nombre correcto.
    private static readonly IReadOnlyDictionary<string, string> TargetExercises =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Sentadilla 1RM"] = "Sentadilla con barra"
        };

    public static async Task SeedAsync(AppDbContext context, ILogger logger)
    {
        // IgnoreQueryFilters: el filtro global ocultaría los registros ya sembrados en corridas previas.
        if (await context.CatalogTests.IgnoreQueryFilters().AnyAsync())
        {
            await BackfillTargetExercisesAsync(context, logger);
            return;
        }

        var tests = new List<CatalogTest>
        {
            // Fuerza
            new("Sentadilla Baja con Barra", MotorCapacity.Strength, "kg", 3),
            new("Sentadilla 1RM", MotorCapacity.Strength, "kg", 3,
                targetExerciseName: TargetExercises["Sentadilla 1RM"]),
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

    // KAN-426: las bases creadas antes del mapeo no pasan por el alta de arriba (el seeder
    // hace early return). Este backfill completa TargetExerciseName en los tests que lo
    // necesitan; es idempotente y no toca ExerciseId (la caché de GUIDs ya resueltos).
    private static async Task BackfillTargetExercisesAsync(AppDbContext context, ILogger logger)
    {
        var pending = await context.CatalogTests.IgnoreQueryFilters()
            .Where(test => test.TargetExerciseName == null && test.Active)
            .ToListAsync();

        var updated = 0;
        foreach (var test in pending)
        {
            if (TargetExercises.TryGetValue(test.Name, out var targetExercise))
            {
                test.SetTargetExercise(targetExercise);
                updated++;
            }
        }

        if (updated == 0)
            return;

        await context.SaveChangesAsync();
        logger.LogInformation("Se completó el mapeo a ejercicios de Entrenamiento en {Count} pruebas.", updated);
    }
}
