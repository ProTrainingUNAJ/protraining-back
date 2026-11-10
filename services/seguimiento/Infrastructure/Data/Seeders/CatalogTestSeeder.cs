using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Data.Seeders;

public static class CatalogTestSeeder
{
    // Tests de fuerza en kg que generan récord 1RM en Entrenamiento, con el nombre
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
        var tests = BuildTests();

        // IgnoreQueryFilters: el filtro global ocultaría los registros ya sembrados en corridas previas.
        if (await context.CatalogTests.IgnoreQueryFilters().AnyAsync())
        {
            await BackfillTargetExercisesAsync(context, logger);
            await BackfillMissingTestsAsync(context, tests, logger);
            return;
        }

        // Al no haber token en el arranque, SetBaseFields asigna PfId = Guid.Empty:
        // las 17 pruebas quedan como catálogo global visibles para todos los PF.
        await context.CatalogTests.AddRangeAsync(tests);
        await context.SaveChangesAsync();
        logger.LogInformation("Se sembraron {Count} pruebas del catálogo motor.", tests.Count);
    }

    // Catálogo completo del microservicio: se reutiliza en el alta inicial y en el
    // backfill de bases que ya fueron sembradas con menos pruebas (KAN-458).
    private static List<CatalogTest> BuildTests()
    {
        return new List<CatalogTest>
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
            new("Carrera 100 m", MotorCapacity.Velocity, "s", 3),
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
    }

    // KAN-458: las bases ya sembradas no repiten el alta de arriba (el seeder hace
    // early return), así que este backfill agrega las pruebas nuevas que falten,
    // identificadas por nombre exacto. Es idempotente: no duplica ni toca las existentes
    // y SetBaseFields les asigna PfId = Guid.Empty (catálogo global), igual que al sembrar.
    private static async Task BackfillMissingTestsAsync(
        AppDbContext context,
        IReadOnlyList<CatalogTest> tests,
        ILogger logger)
    {
        var existingNames = new HashSet<string>(
            await context.CatalogTests.IgnoreQueryFilters().Select(test => test.Name).ToListAsync(),
            StringComparer.Ordinal);

        var missing = tests.Where(test => !existingNames.Contains(test.Name)).ToList();
        if (missing.Count == 0)
            return;

        await context.CatalogTests.AddRangeAsync(missing);
        await context.SaveChangesAsync();
        logger.LogInformation("Se agregaron {Count} pruebas nuevas al catálogo motor.", missing.Count);
    }

    // Las bases creadas antes del mapeo no pasan por el alta de arriba (el seeder
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
