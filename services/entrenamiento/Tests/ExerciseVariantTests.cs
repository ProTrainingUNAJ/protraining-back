using Application.DTOs.Exercise;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Exercise.ListExercises;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Data;
using Infrastructure.Data.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace Tests;

// KAN-554: variantes de ejercicio como "hijos" de un ejercicio base (dominio, seeder y listado agrupado).
public class ExerciseVariantTests
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

    private static string WriteVariantsFile(object families)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "exercise-variants.json"), JsonSerializer.Serialize(families));
        return dir;
    }

    private static string FindRealDataDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data"),
            Path.Combine(Directory.GetCurrentDirectory(), "API", "Data"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "API", "Data"),
        };
        return candidates.First(dir => File.Exists(Path.Combine(dir, "exercise-variants.json")));
    }

    // ── Dominio ──

    [Fact]
    public void Given_BaseExercise_When_AssigningVariant_Then_KeepsIdAndSetsParentAndLabel()
    {
        var bench = Exercise.Create("Press de banca con barra - Agarre medio", "Pecho");
        var closeGrip = Exercise.Create("Press de banca con barra con agarre cerrado", "Pecho");
        var originalId = closeGrip.Id;

        closeGrip.AssignAsVariantOf(bench, "  Agarre cerrado ");

        Assert.Equal(originalId, closeGrip.Id);
        Assert.Equal(bench.Id, closeGrip.ParentExerciseId);
        Assert.Equal("Agarre cerrado", closeGrip.VariantLabel);
        Assert.True(closeGrip.IsVariant);
        Assert.False(bench.IsVariant);
    }

    [Fact]
    public void Given_SameExercise_When_AssigningAsItsOwnVariant_Then_Throws()
    {
        var bench = Exercise.Create("Press de banca", "Pecho");

        Assert.Throws<DomainException>(() => bench.AssignAsVariantOf(bench, "Agarre medio"));
    }

    [Fact]
    public void Given_BaseThatIsAVariant_When_AssigningVariant_Then_ThrowsBecauseOnlyOneLevel()
    {
        var bench = Exercise.Create("Press de banca", "Pecho");
        var incline = Exercise.Create("Press de banca inclinado", "Pecho");
        var inclineDumbbell = Exercise.Create("Press de banca inclinado con mancuernas", "Pecho");
        incline.AssignAsVariantOf(bench, "Inclinado");

        Assert.Throws<DomainException>(() => inclineDumbbell.AssignAsVariantOf(incline, "Con mancuernas"));
    }

    [Fact]
    public void Given_ExerciseWithOwnVariants_When_AssigningAsVariant_Then_Throws()
    {
        var bench = Exercise.Create("Press de banca", "Pecho");
        var squat = Exercise.Create("Sentadilla con barra", "Piernas");

        Assert.Throws<DomainException>(() => squat.AssignAsVariantOf(bench, "Otra", hasOwnVariants: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_EmptyLabel_When_AssigningVariant_Then_Throws(string label)
    {
        var bench = Exercise.Create("Press de banca", "Pecho");
        var closeGrip = Exercise.Create("Press de banca agarre cerrado", "Pecho");

        Assert.Throws<DomainException>(() => closeGrip.AssignAsVariantOf(bench, label));
    }

    // ── Seeder ──

    [Fact]
    public async Task Given_CatalogAlreadyLoaded_When_SeedingVariants_Then_GroupsByNameWithoutChangingIds()
    {
        var dbName = Guid.NewGuid().ToString();
        var bench = Exercise.Create("Press de banca con barra - Agarre medio", "Pecho");
        var closeGrip = Exercise.Create("Press de banca con barra con agarre cerrado", "Pecho");
        var squat = Exercise.Create("Sentadilla con barra", "Piernas");

        await using (var context = CreateInMemoryDbContext(dbName))
        {
            await context.Exercises.AddRangeAsync(bench, closeGrip, squat);
            await context.SaveChangesAsync();
        }

        var dir = WriteVariantsFile(new[]
        {
            new
            {
                @base = "Press de banca con barra - Agarre medio",
                variants = new[] { new { name = "Press de banca con barra con agarre cerrado", label = "Agarre cerrado" } }
            }
        });

        try
        {
            await using (var context = CreateInMemoryDbContext(dbName))
            {
                await ExerciseVariantSeeder.SeedAsync(context, dir, new TestLogger());
            }

            await using var query = CreateInMemoryDbContext(dbName);
            var stored = await query.Exercises.IgnoreQueryFilters().ToDictionaryAsync(e => e.Id);

            Assert.Equal(bench.Id, stored[closeGrip.Id].ParentExerciseId);
            Assert.Equal("Agarre cerrado", stored[closeGrip.Id].VariantLabel);
            Assert.Null(stored[bench.Id].ParentExerciseId);
            Assert.Null(stored[squat.Id].ParentExerciseId);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Given_MissingNames_When_SeedingVariants_Then_WarnsAndContinues()
    {
        var dbName = Guid.NewGuid().ToString();
        var bench = Exercise.Create("Press de banca con barra - Agarre medio", "Pecho");
        var closeGrip = Exercise.Create("Press de banca con barra con agarre cerrado", "Pecho");

        await using (var context = CreateInMemoryDbContext(dbName))
        {
            await context.Exercises.AddRangeAsync(bench, closeGrip);
            await context.SaveChangesAsync();
        }

        var dir = WriteVariantsFile(new[]
        {
            new
            {
                @base = "Ejercicio que no existe",
                variants = new[] { new { name = "Press de banca con barra con agarre cerrado", label = "Agarre cerrado" } }
            },
            new
            {
                @base = "Press de banca con barra - Agarre medio",
                variants = new[]
                {
                    new { name = "Variante que no existe", label = "X" },
                    new { name = "Press de banca con barra con agarre cerrado", label = "Agarre cerrado" }
                }
            }
        });
        var logger = new TestLogger();

        try
        {
            await using (var context = CreateInMemoryDbContext(dbName))
            {
                await ExerciseVariantSeeder.SeedAsync(context, dir, logger);
            }

            await using var query = CreateInMemoryDbContext(dbName);
            var stored = await query.Exercises.IgnoreQueryFilters().SingleAsync(e => e.Id == closeGrip.Id);

            Assert.Equal(bench.Id, stored.ParentExerciseId);
            Assert.Contains(logger.Logs, l => l.Level == LogLevel.Warning && l.Message.Contains("Ejercicio que no existe"));
            Assert.Contains(logger.Logs, l => l.Level == LogLevel.Warning && l.Message.Contains("Variante que no existe"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Given_VariantRemovedFromFile_When_SeedingAgain_Then_ItBecomesAStandaloneExercise()
    {
        var dbName = Guid.NewGuid().ToString();
        var lunge = Exercise.Create("Zancadas con mancuernas", "Piernas");
        var crossover = Exercise.Create("Zancada inversa cruzada", "Piernas");
        var barbell = Exercise.Create("Zancada con barra", "Piernas");

        await using (var context = CreateInMemoryDbContext(dbName))
        {
            await context.Exercises.AddRangeAsync(lunge, crossover, barbell);
            await context.SaveChangesAsync();
        }

        var before = WriteVariantsFile(new[]
        {
            new
            {
                @base = "Zancadas con mancuernas",
                variants = new[]
                {
                    new { name = "Zancada inversa cruzada", label = "Inversa cruzada" },
                    new { name = "Zancada con barra", label = "Con barra" }
                }
            }
        });
        var after = WriteVariantsFile(new[]
        {
            new
            {
                @base = "Zancadas con mancuernas",
                variants = new[] { new { name = "Zancada con barra", label = "Con barra" } }
            }
        });

        try
        {
            await using (var context = CreateInMemoryDbContext(dbName))
            {
                await ExerciseVariantSeeder.SeedAsync(context, before, new TestLogger());
            }
            await using (var context = CreateInMemoryDbContext(dbName))
            {
                await ExerciseVariantSeeder.SeedAsync(context, after, new TestLogger());
            }

            await using var query = CreateInMemoryDbContext(dbName);
            var stored = await query.Exercises.IgnoreQueryFilters().ToDictionaryAsync(e => e.Id);

            Assert.Null(stored[crossover.Id].ParentExerciseId);
            Assert.Null(stored[crossover.Id].VariantLabel);
            Assert.Equal(lunge.Id, stored[barbell.Id].ParentExerciseId);
        }
        finally
        {
            Directory.Delete(before, true);
            Directory.Delete(after, true);
        }
    }

    [Fact]
    public async Task Given_RealFiles_When_SeedingTwice_Then_AllVariantsGroupedAndSecondRunChangesNothing()
    {
        var dbName = Guid.NewGuid().ToString();
        var dataDirectory = FindRealDataDirectory();

        await using (var context = CreateInMemoryDbContext(dbName))
        {
            await ExerciseCatalogSeeder.SeedAsync(context, dataDirectory, new TestLogger());
            await ExerciseVariantSeeder.SeedAsync(context, dataDirectory, new TestLogger());
        }

        var secondRunLogger = new TestLogger();
        await using (var context = CreateInMemoryDbContext(dbName))
        {
            await ExerciseVariantSeeder.SeedAsync(context, dataDirectory, secondRunLogger);
        }

        var families = JsonSerializer.Deserialize<List<JsonElement>>(
            await File.ReadAllTextAsync(Path.Combine(dataDirectory, "exercise-variants.json")))!;

        await using var query = CreateInMemoryDbContext(dbName);
        var catalog = await query.Exercises.IgnoreQueryFilters().ToListAsync();
        var bases = catalog.Where(e => e.ParentExerciseId == null).ToDictionary(e => e.Id);

        foreach (var family in families)
        {
            var baseName = family.GetProperty("base").GetString()!;
            var baseExercise = Assert.Single(catalog, e => e.Name == baseName);
            Assert.False(baseExercise.IsVariant);

            foreach (var variant in family.GetProperty("variants").EnumerateArray())
            {
                var name = variant.GetProperty("name").GetString()!;
                var matches = catalog.Where(e => e.Name == name).ToList();
                Assert.NotEmpty(matches);
                Assert.All(matches, m => Assert.Equal(baseExercise.Id, m.ParentExerciseId));
            }
        }

        // Ninguna familia repite una etiqueta (los nombres duplicados por la traducción se corrigieron).
        foreach (var family in catalog.Where(e => e.IsVariant).GroupBy(e => e.ParentExerciseId))
        {
            Assert.Equal(family.Count(), family.Select(v => v.VariantLabel).Distinct().Count());
        }
        // Un solo nivel: ninguna variante apunta a otra variante.
        Assert.All(catalog.Where(e => e.IsVariant), v => Assert.True(bases.ContainsKey(v.ParentExerciseId!.Value)));
        // Los tres ejercicios del script de la demo siguen siendo base.
        Assert.Contains(bases.Values, e => e.Name == "Sentadilla con barra");
        Assert.Contains(bases.Values, e => e.Name == "Press de banca con barra - Agarre medio");
        Assert.Contains(bases.Values, e => e.Name == "Peso muerto rumano");
        // La segunda corrida no cambia nada ni deja avisos.
        Assert.DoesNotContain(secondRunLogger.Logs, l => l.Level >= LogLevel.Information);
    }

    // ── Nombres del catálogo ──

    [Fact]
    public void Given_ImageUrls_When_ReadingSourceId_Then_ReturnsTheFreeExerciseDbFolder()
    {
        var images = new List<string>
        {
            "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Dumbbell_Floor_Press/0.jpg"
        };

        Assert.Equal("Dumbbell_Floor_Press", ExerciseNameSyncSeeder.SourceIdFromImages(images));
        Assert.Null(ExerciseNameSyncSeeder.SourceIdFromImages(new List<string>()));
    }

    [Fact]
    public async Task Given_TranslationFixedInJson_When_SyncingNames_Then_RenamesOnlyThatExerciseAndKeepsItsId()
    {
        var dbName = Guid.NewGuid().ToString();
        const string imagesRoot = "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/";
        var bench = Exercise.Create("Press de banca con mancuernas", "Pecho",
            images: [imagesRoot + "Dumbbell_Bench_Press/0.jpg"]);
        var floor = Exercise.Create("Press de banca con mancuernas", "Pecho",
            images: [imagesRoot + "Dumbbell_Floor_Press/0.jpg"]);
        var custom = Exercise.Create("Ejercicio sin fotos", "Core");

        await using (var context = CreateInMemoryDbContext(dbName))
        {
            await context.Exercises.AddRangeAsync(bench, floor, custom);
            await context.SaveChangesAsync();
        }

        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "exercises.json"), JsonSerializer.Serialize(new[]
        {
            new { id = "Dumbbell_Bench_Press", name = "Press de banca con mancuernas" },
            new { id = "Dumbbell_Floor_Press", name = "Press de banca con mancuernas en el piso" },
        }));

        try
        {
            await using (var context = CreateInMemoryDbContext(dbName))
            {
                await ExerciseNameSyncSeeder.SeedAsync(context, dir, new TestLogger());
            }

            await using var query = CreateInMemoryDbContext(dbName);
            var stored = await query.Exercises.IgnoreQueryFilters().ToDictionaryAsync(e => e.Id);

            Assert.Equal("Press de banca con mancuernas en el piso", stored[floor.Id].Name);
            Assert.Equal("Press de banca con mancuernas", stored[bench.Id].Name);
            Assert.Equal("Ejercicio sin fotos", stored[custom.Id].Name);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── Listado ──

    [Fact]
    public async Task Given_GroupVariantsTrue_When_Listing_Then_ReturnsBasesWithTheirVariants()
    {
        var bench = Exercise.Create("Press de banca con barra - Agarre medio", "Pecho");
        var closeGrip = Exercise.Create("Press de banca con barra con agarre cerrado", "Pecho");
        var dumbbell = Exercise.Create("Press de banca con mancuernas", "Pecho");
        closeGrip.AssignAsVariantOf(bench, "Agarre cerrado");
        dumbbell.AssignAsVariantOf(bench, "Con mancuernas");

        var repoMock = new Mock<IExerciseRepository>();
        repoMock.Setup(r => r.ListBasesAsync("press de banca", null, null, false, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Exercise> { bench }, 1));
        repoMock.Setup(r => r.GetVariantsAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(bench.Id)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Exercise> { closeGrip, dumbbell });

        var handler = new ListExercisesQueryHandler(repoMock.Object);
        var result = await handler.Handle(new ListExercisesQuery { Search = "press de banca", GroupVariants = true });

        var item = Assert.Single(result.Items);
        Assert.Equal(bench.Id, item.Id);
        Assert.NotNull(item.Variants);
        Assert.Equal(2, item.Variants!.Count);
        Assert.Contains(item.Variants, v => v.Id == closeGrip.Id && v.VariantLabel == "Agarre cerrado" && v.ParentExerciseId == bench.Id);
        Assert.Equal(1, result.TotalCount);
        repoMock.Verify(r => r.ListAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_GroupVariantsFalse_When_Listing_Then_KeepsFlatListWithVariantFields()
    {
        var bench = Exercise.Create("Press de banca con barra - Agarre medio", "Pecho");
        var closeGrip = Exercise.Create("Press de banca con barra con agarre cerrado", "Pecho");
        closeGrip.AssignAsVariantOf(bench, "Agarre cerrado");

        var repoMock = new Mock<IExerciseRepository>();
        repoMock.Setup(r => r.ListAsync(null, null, null, false, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Exercise> { bench, closeGrip }, 2));

        var handler = new ListExercisesQueryHandler(repoMock.Object);
        var result = await handler.Handle(new ListExercisesQuery());

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.Null(i.Variants));
        Assert.Equal(bench.Id, result.Items.Single(i => i.Id == closeGrip.Id).ParentExerciseId);
        repoMock.Verify(r => r.ListBasesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
