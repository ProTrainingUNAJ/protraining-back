using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Data.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Xunit;

namespace Tests;

public class ExerciseRestrictionSeederTests
{
    private sealed class TestCurrentUser(Guid? pfId = null) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    private sealed class TestLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Logs { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Logs.Add((logLevel, formatter(state, exception)));
        }
    }

    private static AppDbContext CreateInMemoryDbContext(string dbName, Guid? pfId = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new AppDbContext(options, new TestCurrentUser(pfId));
    }

    [Fact]
    public async Task Seed_ZonaCuadriceps_RestringeSentadillaConPrensa()
    {
        // Criterio de aceptación 1:
        // DADO el seed ejecutado CUANDO se consulta la zona cuadriceps ENTONCES la sentadilla aparece restringida y la prensa de piernas como sustituto.
        var dbName = Guid.NewGuid().ToString();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var sentadilla = Exercise.Create("Sentadilla con barra", "Piernas", "Sentadilla tradicional");
            var prensa = Exercise.Create("Prensa de piernas", "Piernas", "Prensa 45");
            var bandas = Exercise.Create("Sentadillas con bandas elásticas", "Piernas", "Sentadilla con banda");

            await using (var context = CreateInMemoryDbContext(dbName))
            {
                await context.Exercises.AddRangeAsync(sentadilla, prensa, bandas);
                await context.SaveChangesAsync();
            }

            var restrictionsJson = JsonSerializer.Serialize(new[]
            {
                new
                {
                    zone = "cuadriceps",
                    exercise = "Sentadilla con barra",
                    minPainLevel = 1,
                    reason = "Sobrecarga patelofemoral",
                    substitutes = new[] { "Prensa de piernas", "Sentadillas con bandas elásticas" }
                }
            });
            await File.WriteAllTextAsync(Path.Combine(tempDir, "exercise-restrictions.json"), restrictionsJson);

            var logger = new TestLogger();

            await using (var context = CreateInMemoryDbContext(dbName))
            {
                await ExerciseRestrictionSeeder.SeedAsync(context, tempDir, logger);
            }

            // Assert (incluso consultando como un PF logueado)
            await using (var queryContext = CreateInMemoryDbContext(dbName, Guid.NewGuid()))
            {
                var restrictions = await queryContext.ExerciseRestrictions
                    .Include(r => r.Exercise)
                    .Include(r => r.Substitutes)
                        .ThenInclude(s => s.SubstituteExercise)
                    .Where(r => r.Zone == "cuadriceps")
                    .ToListAsync();

                var sentadillaRestriction = restrictions.FirstOrDefault(r => r.Exercise.Name.Contains("Sentadilla"));
                Assert.NotNull(sentadillaRestriction);
                Assert.Contains(sentadillaRestriction.Substitutes, s => s.SubstituteExercise.Name == "Prensa de piernas");
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Seed_ZonaEspaldaBaja_RestringePesoMuertoConSustitutos()
    {
        // Criterio de aceptación 2:
        // DADO el seed ejecutado CUANDO se consulta la zona espalda_baja ENTONCES el peso muerto aparece restringido, con al menos 2 sustitutos.
        var dbName = Guid.NewGuid().ToString();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var pesoMuerto = Exercise.Create("Peso muerto con barra", "Espalda", "Peso muerto convencional");
            var prensa = Exercise.Create("Prensa de piernas", "Piernas", "Prensa");
            var puente = Exercise.Create("Puente de glúteos con barra", "Piernas", "Puente de glúteos");

            await using (var context = CreateInMemoryDbContext(dbName))
            {
                await context.Exercises.AddRangeAsync(pesoMuerto, prensa, puente);
                await context.SaveChangesAsync();
            }

            var restrictionsJson = JsonSerializer.Serialize(new[]
            {
                new
                {
                    zone = "espalda_baja",
                    exercise = "Peso muerto con barra",
                    minPainLevel = 1,
                    reason = "Fuerzas de cizallamiento lumbares",
                    substitutes = new[] { "Prensa de piernas", "Puente de glúteos con barra" }
                }
            });
            await File.WriteAllTextAsync(Path.Combine(tempDir, "exercise-restrictions.json"), restrictionsJson);

            var logger = new TestLogger();

            await using (var context = CreateInMemoryDbContext(dbName))
            {
                await ExerciseRestrictionSeeder.SeedAsync(context, tempDir, logger);
            }

            // Assert
            await using (var queryContext = CreateInMemoryDbContext(dbName, Guid.NewGuid()))
            {
                var restrictions = await queryContext.ExerciseRestrictions
                    .Include(r => r.Exercise)
                    .Include(r => r.Substitutes)
                        .ThenInclude(s => s.SubstituteExercise)
                    .Where(r => r.Zone == "espalda_baja")
                    .ToListAsync();

                var pesoMuertoRestriction = restrictions.FirstOrDefault(r => r.Exercise.Name.Contains("Peso muerto"));
                Assert.NotNull(pesoMuertoRestriction);
                Assert.True(pesoMuertoRestriction.Substitutes.Count >= 2);
                Assert.Contains(pesoMuertoRestriction.Substitutes, s => s.SubstituteExercise.Name == "Prensa de piernas");
                Assert.Contains(pesoMuertoRestriction.Substitutes, s => s.SubstituteExercise.Name == "Puente de glúteos con barra");
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Seed_EjercicioInexistente_RegistraAvisoYSigue()
    {
        // Criterio de aceptación 3:
        // DADO un nombre de ejercicio que no está en el catálogo CUANDO corre el seed ENTONCES se registra un aviso y el servicio arranca igual.
        var dbName = Guid.NewGuid().ToString();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var ejercicioValido = Exercise.Create("Sentadilla con barra", "Piernas", "Sentadilla");
            var sustitutoValido = Exercise.Create("Prensa de piernas", "Piernas", "Prensa");

            await using (var context = CreateInMemoryDbContext(dbName))
            {
                await context.Exercises.AddRangeAsync(ejercicioValido, sustitutoValido);
                await context.SaveChangesAsync();
            }

            var restrictionsJson = JsonSerializer.Serialize(new[]
            {
                new
                {
                    zone = "cuadriceps",
                    exercise = "Ejercicio Fantasma Inexistente",
                    minPainLevel = 1,
                    reason = "Motivo inventado",
                    substitutes = new[] { "Prensa de piernas" }
                },
                new
                {
                    zone = "cuadriceps",
                    exercise = "Sentadilla con barra",
                    minPainLevel = 1,
                    reason = "Sobrecarga patelar",
                    substitutes = new[] { "Sustituto Que No Existe", "Prensa de piernas" }
                }
            });
            await File.WriteAllTextAsync(Path.Combine(tempDir, "exercise-restrictions.json"), restrictionsJson);

            var logger = new TestLogger();

            await using (var context = CreateInMemoryDbContext(dbName))
            {
                // No debe lanzar excepción
                await ExerciseRestrictionSeeder.SeedAsync(context, tempDir, logger);
            }

            // Debe registrar avisos en el log
            Assert.Contains(logger.Logs, l =>
                l.Level == LogLevel.Warning &&
                l.Message.Contains("No se encontró el ejercicio 'Ejercicio Fantasma Inexistente'"));

            Assert.Contains(logger.Logs, l =>
                l.Level == LogLevel.Warning &&
                l.Message.Contains("No se encontró el ejercicio sustituto 'Sustituto Que No Existe'"));

            // Y la restricción válida debe haberse guardado con el sustituto disponible
            await using (var queryContext = CreateInMemoryDbContext(dbName, Guid.NewGuid()))
            {
                var restrictions = await queryContext.ExerciseRestrictions
                    .Include(r => r.Substitutes)
                    .ToListAsync();

                Assert.Single(restrictions);
                Assert.Single(restrictions[0].Substitutes);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Seed_TablaConDatos_NoDuplica()
    {
        var dbName = Guid.NewGuid().ToString();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var sentadilla = Exercise.Create("Sentadilla con barra", "Piernas", "Sentadilla");
            var restrictionExistente = ExerciseRestriction.Create("cuadriceps", sentadilla.Id, "Existente", 1);

            await using (var context = CreateInMemoryDbContext(dbName))
            {
                await context.Exercises.AddAsync(sentadilla);
                await context.ExerciseRestrictions.AddAsync(restrictionExistente);
                await context.SaveChangesAsync();
            }

            var restrictionsJson = JsonSerializer.Serialize(new[]
            {
                new
                {
                    zone = "cuadriceps",
                    exercise = "Sentadilla con barra",
                    minPainLevel = 1,
                    reason = "Nueva razón",
                    substitutes = Array.Empty<string>()
                }
            });
            await File.WriteAllTextAsync(Path.Combine(tempDir, "exercise-restrictions.json"), restrictionsJson);

            var logger = new TestLogger();

            await using (var context = CreateInMemoryDbContext(dbName))
            {
                await ExerciseRestrictionSeeder.SeedAsync(context, tempDir, logger);
            }

            await using (var queryContext = CreateInMemoryDbContext(dbName, Guid.NewGuid()))
            {
                var count = await queryContext.ExerciseRestrictions.CountAsync();
                Assert.Equal(1, count);

                var item = await queryContext.ExerciseRestrictions.FirstAsync();
                Assert.Equal("Existente", item.Reason);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Seed_ArchivoInexistente_RegistraAviso()
    {
        var dbName = Guid.NewGuid().ToString();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var logger = new TestLogger();

        await using (var context = CreateInMemoryDbContext(dbName))
        {
            await ExerciseRestrictionSeeder.SeedAsync(context, tempDir, logger);
        }

        Assert.Contains(logger.Logs, l => l.Level == LogLevel.Warning && l.Message.Contains("No se encontró el archivo de restricciones"));
    }

    [Fact]
    public async Task Seed_ArchivoReal_CumpleCriteriosPorZona()
    {
        var dbName = Guid.NewGuid().ToString();
        var logger = new TestLogger();

        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        if (!File.Exists(Path.Combine(dataDirectory, "exercises.json")))
        {
            dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "API", "Data");
            if (!File.Exists(Path.Combine(dataDirectory, "exercises.json")))
            {
                dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "..", "API", "Data");
            }
        }

        await using (var context = CreateInMemoryDbContext(dbName))
        {
            // Primero se carga el catálogo
            await ExerciseCatalogSeeder.SeedAsync(context, dataDirectory, logger);
            // Luego el seed de restricciones
            await ExerciseRestrictionSeeder.SeedAsync(context, dataDirectory, logger);
        }

        await using (var queryContext = CreateInMemoryDbContext(dbName, Guid.NewGuid()))
        {
            var restrictions = await queryContext.ExerciseRestrictions
                .Include(r => r.Exercise)
                .Include(r => r.Substitutes)
                    .ThenInclude(s => s.SubstituteExercise)
                .ToListAsync();

            Assert.NotEmpty(restrictions);

            var requiredZones = new[] { "cuadriceps", "isquiotibiales", "hombros", "espalda_baja" };

            foreach (var zone in requiredZones)
            {
                var zoneRestrictions = restrictions.Where(r => r.Zone == zone).ToList();
                Assert.True(zoneRestrictions.Count >= 5, $"La zona '{zone}' debe tener al menos 5 ejercicios restringidos, pero tiene {zoneRestrictions.Count}.");

                foreach (var r in zoneRestrictions)
                {
                    Assert.True(r.Substitutes.Count >= 2, $"El ejercicio '{r.Exercise.Name}' en la zona '{zone}' debe tener al menos 2 sustitutos, pero tiene {r.Substitutes.Count}.");
                }
            }

            // KAN-452: saltos restringidos por cuadriceps, con prensa de piernas o ejercicios con bandas como sustitutos
            var cuadricepsRestrictions = restrictions.Where(r => r.Zone == "cuadriceps").ToList();
            var jumpRestrictions = cuadricepsRestrictions.Where(r => r.Exercise.Name.ToLowerInvariant().Contains("salto")).ToList();
            Assert.NotEmpty(jumpRestrictions);

            foreach (var jump in jumpRestrictions)
            {
                var substituteNames = jump.Substitutes.Select(s => s.SubstituteExercise.Name.ToLowerInvariant()).ToList();
                var hasPrensaOrBandas = substituteNames.Any(name => name.Contains("prensa") || name.Contains("banda"));
                Assert.True(hasPrensaOrBandas, $"El salto '{jump.Exercise.Name}' debe tener prensa o bandas como sustituto.");
            }
        }
    }
}
