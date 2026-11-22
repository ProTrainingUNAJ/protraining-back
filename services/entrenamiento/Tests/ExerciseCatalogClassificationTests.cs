using Application.Interfaces.Services;
using Application.UseCases.Exercise.ListExercises;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Data.Seeders;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Entrenamiento.Tests;

// KAN-590: Pruebas de clasificación de básicos y búsqueda de movilidad / activación / entrada en calor.
public sealed class ExerciseCatalogClassificationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;

    private sealed class TestCurrentUser(Guid? pfId) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    public ExerciseCatalogClassificationTests()
    {
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private AppDbContext CreateContext() => new(_options, new TestCurrentUser(null));

    [Theory]
    // Peso muerto y todas sus variantes -> Piernas (cadena posterior)
    [InlineData("Peso muerto con barra", "Barbell_Deadlift", "Piernas")]
    [InlineData("Peso muerto rumano", "Romanian_Deadlift", "Piernas")]
    [InlineData("Peso muerto con eje", "Axle_Deadlift", "Piernas")]
    [InlineData("Peso muerto sumo", "Sumo_Deadlift", "Piernas")]
    [InlineData("Peso muerto con déficit", "Deficit_Deadlift", "Piernas")]
    [InlineData("Peso muerto con barra hexagonal", "Trap_Bar_Deadlift", "Piernas")]
    [InlineData("Peso muerto con bandas", "Deadlift_with_Bands", "Piernas")]
    [InlineData("Peso muerto con cadenas", "Deadlift_with_Chains", "Piernas")]
    [InlineData("Peso muerto con piernas rígidas en máquina Smith", "Smith_Machine_Stiff-Legged_Deadlift", "Piernas")]
    // Hip thrust y puente de glúteo -> Piernas
    [InlineData("Empuje de cadera con barra", "Barbell_Hip_Thrust", "Piernas")]
    [InlineData("Puente de glúteos con barra", "Barbell_Glute_Bridge", "Piernas")]
    [InlineData("Puente de glúteo (activación)", "Butt_Lift_Bridge", "Piernas")]
    [InlineData("Puente de glúteos con una sola pierna", "Single_Leg_Glute_Bridge", "Piernas")]
    // Movilidad de columna -> Core
    [InlineData("Gato-camello (movilidad de columna)", "Cat_Stretch", "Core")]
    // Sentadillas y variantes -> Piernas
    [InlineData("Sentadilla con barra", "Barbell_Squat", "Piernas")]
    [InlineData("Sentadilla frontal con barra", "Front_Barbell_Squat", "Piernas")]
    [InlineData("Sentadilla con copa", "Goblet_Squat", "Piernas")]
    [InlineData("Prensa de piernas", "Leg_Press", "Piernas")]
    // Press de banca y fondos -> Pecho (variantes de tríceps -> Brazos)
    [InlineData("Press de banca con barra - Agarre medio", "Barbell_Bench_Press_-_Medium_Grip", "Pecho")]
    [InlineData("Press de banca con barra con agarre cerrado", "Close-Grip_Barbell_Bench_Press", "Pecho")]
    [InlineData("Press de banca inclinado con barra - Agarre medio", "Barbell_Incline_Bench_Press_-_Medium_Grip", "Pecho")]
    [InlineData("Press de banca con mancuernas", "Dumbbell_Bench_Press", "Pecho")]
    [InlineData("Press de banca con cadenas", "Bench_Press_with_Chains", "Pecho")]
    [InlineData("Press de banca - Levantamiento de pesas", "Bench_Press_-_Powerlifting", "Pecho")]
    [InlineData("Press de banca con mancuernas en el piso", "Dumbbell_Floor_Press", "Pecho")]
    [InlineData("Press de banca en máquina Smith", "Smith_Machine_Bench_Press", "Pecho")]
    [InlineData("Fondos - Versión de pecho", "Dips_-_Chest_Version", "Pecho")]
    // Variantes de tríceps -> Brazos
    [InlineData("Fondos - Versión para tríceps", "Dips_-_Triceps_Version", "Brazos")]
    [InlineData("fondos en banco", "Bench_Dips", "Brazos")]
    [InlineData("Press de banca inverso para tríceps", "Reverse_Triceps_Bench_Press", "Brazos")]
    [InlineData("Press de banca con agarre cerrado declinado a máquina de press de banca", "Decline_Close-Grip_Bench_To_Skull_Crusher", "Brazos")]
    // Press militar y deltoides posteriores -> Hombros
    [InlineData("Press militar con barra sentado", "Seated_Barbell_Military_Press", "Hombros")]
    [InlineData("Prensa militar permanente", "Standing_Military_Press", "Hombros")]
    [InlineData("Prensa de hombro a máquina (militar)", "Machine_Shoulder_Military_Press", "Hombros")]
    [InlineData("Press militar con pesa rusa a dos brazos", "Two-Arm_Kettlebell_Military_Press", "Hombros")]
    [InlineData("Remo con barra para deltoides posteriores", "Barbell_Rear_Delt_Row", "Hombros")]
    [InlineData("Remo con cuerda y cable para deltoides posteriores", "Cable_Rope_Rear-Delt_Rows", "Hombros")]
    // Dominadas, remos y jalones -> Espalda
    [InlineData("Dominadas", "Pullups", "Espalda")]
    [InlineData("dominadas", "Chin-up", "Espalda")]
    [InlineData("Dominadas asistidas con banda", "Band_Assisted_Pull-Up", "Espalda")]
    [InlineData("Remo con barra inclinado", "Bent_Over_Barbell_Row", "Espalda")]
    [InlineData("Remo con dos mancuernas inclinado", "Bent_Over_Two-Dumbbell_Row", "Espalda")]
    [InlineData("Remo con barra en T y asa", "T-Bar_Row_with_Handle", "Espalda")]
    [InlineData("Jalón al pecho con agarre ancho", "Wide-Grip_Lat_Pulldown", "Espalda")]
    [InlineData("Jalón al pecho frontal con agarre cerrado", "Close-Grip_Front_Lat_Pulldown", "Espalda")]
    public void DetermineMuscleGroup_ClasificaBasicosSegunReglasDelPM(string name, string sourceId, string expectedGroup)
    {
        // ACT
        var result = ExerciseClassificationHelper.DetermineMuscleGroup(name, sourceId);

        // ASSERT
        Assert.Equal(expectedGroup, result);
    }

    [Fact]
    public async Task Seeder_ReclasificaBasicosExistentesYAgregaEjerciciosDeMovilidad()
    {
        // ARRANGE: sembramos ejercicios con clasificación vieja errónea y un ejercicio de activación renombrado que venía con Weight (%RM)
        await using (var seedContext = CreateContext())
        {
            var pesoMuertoViejo = Exercise.Create("Peso muerto con barra", "Espalda", origin: Origin.Catalog);
            var pressCerradoViejo = Exercise.Create("Press de banca con barra con agarre cerrado", "Brazos", origin: Origin.Catalog);
            var oldBenchDips = Exercise.Create("fondos en banco", "Pecho", origin: Origin.Catalog);
            var oldRearDeltRow = Exercise.Create("Remo con barra para deltoides posteriores", "Espalda", origin: Origin.Catalog);
            var facePullViejo = Exercise.Create("Face pull con banda (activación)", "Hombros", origin: Origin.Catalog, exerciseType: ExerciseType.Weight);

            await seedContext.Exercises.AddRangeAsync(pesoMuertoViejo, pressCerradoViejo, oldBenchDips, oldRearDeltRow, facePullViejo);
            await seedContext.SaveChangesAsync();
        }

        // ACT: Corremos el seeder de KAN-590
        await using (var runContext = CreateContext())
        {
            await MobilityAndBasicExerciseSeeder.SeedAsync(runContext, NullLogger.Instance);
        }

        // ASSERT: Verificamos en base de datos
        await using (var verifyContext = CreateContext())
        {
            var pm = await verifyContext.Exercises.FirstAsync(e => e.Name == "Peso muerto con barra");
            Assert.Equal("Piernas", pm.MuscleGroup);

            var bench = await verifyContext.Exercises.FirstAsync(e => e.Name == "Press de banca con barra con agarre cerrado");
            Assert.Equal("Pecho", bench.MuscleGroup);

            var dips = await verifyContext.Exercises.FirstAsync(e => e.Name == "fondos en banco");
            Assert.Equal("Brazos", dips.MuscleGroup);

            var rearDelt = await verifyContext.Exercises.FirstAsync(e => e.Name == "Remo con barra para deltoides posteriores");
            Assert.Equal("Hombros", rearDelt.MuscleGroup);

            var facePull = await verifyContext.Exercises.FirstAsync(e => e.Name == "Face pull con banda (activación)");
            Assert.Equal(ExerciseType.Reps, facePull.ExerciseType);

            // Verificamos que los 13 ejercicios requeridos por el PM existan y no tengan %RM (Weight)
            var expectedExercises = new[]
            {
                "Movilidad de cadera 90/90",
                "Movilidad de tobillo en pared",
                "Gato-camello (movilidad de columna)",
                "Rotaciones torácicas (movilidad)",
                "\"World's greatest stretch\" (estocada con rotación, movilidad)",
                "Dislocaciones de hombro con palo o banda (movilidad)",
                "Puente de glúteo (activación)",
                "Caminata lateral con banda (monster walk, activación)",
                "Face pull con banda (activación)",
                "Plancha (activación)",
                "Saltos a la soga (entrada en calor)",
                "Jumping jacks (entrada en calor)",
                "Trote suave o bicicleta (5 min, entrada en calor)"
            };

            foreach (var expectedName in expectedExercises)
            {
                var exercise = await verifyContext.Exercises.FirstOrDefaultAsync(e => e.Name == expectedName);
                Assert.NotNull(exercise);
                Assert.NotEqual(ExerciseType.Weight, exercise.ExerciseType); // Sin %RM (son Reps o Time)
            }
        }
    }

    [Fact]
    public async Task Seeder_EsIdempotente_NoDuplicaEjerciciosEnSegundaCorrida()
    {
        // ARRANGE & ACT: ejecutar seeder dos veces seguidas
        await using (var run1 = CreateContext())
        {
            await MobilityAndBasicExerciseSeeder.SeedAsync(run1, NullLogger.Instance);
        }

        int countAfterRun1;
        await using (var verify1 = CreateContext())
        {
            countAfterRun1 = await verify1.Exercises.CountAsync();
            Assert.True(countAfterRun1 >= 13);
        }

        await using (var run2 = CreateContext())
        {
            await MobilityAndBasicExerciseSeeder.SeedAsync(run2, NullLogger.Instance);
        }

        // ASSERT: no hay registros adicionales
        await using (var verify2 = CreateContext())
        {
            var countAfterRun2 = await verify2.Exercises.CountAsync();
            Assert.Equal(countAfterRun1, countAfterRun2);
        }
    }

    [Fact]
    public async Task CatalogoSembrado_ContieneTerminosDeBusquedaYModalidadesSinPorcentajeRm()
    {
        // ARRANGE: sembramos catálogo con los nuevos ejercicios
        await using (var seedContext = CreateContext())
        {
            await MobilityAndBasicExerciseSeeder.SeedAsync(seedContext, NullLogger.Instance);
        }

        await using var queryContext = CreateContext();
        var repository = new ExerciseRepository(queryContext);
        var allCatalog = await repository.ListAllAsync();

        // ACT & ASSERT: Búsqueda por término "movilidad"
        var movilidadItems = allCatalog
            .Where(e => e.Name.Contains("movilidad", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(6, movilidadItems.Count);
        Assert.Contains(movilidadItems, e => e.Name.Contains("Movilidad de cadera 90/90", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(movilidadItems, e => e.Name.Contains("Movilidad de tobillo en pared", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(movilidadItems, e => e.Name.Contains("Gato-camello", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(movilidadItems, e => e.Name.Contains("Rotaciones torácicas", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(movilidadItems, e => e.Name.Contains("World's greatest stretch", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(movilidadItems, e => e.Name.Contains("Dislocaciones de hombro", StringComparison.OrdinalIgnoreCase));
        Assert.All(movilidadItems, e => Assert.Equal(ExerciseType.Reps, e.ExerciseType));

        // ACT & ASSERT: Búsqueda por término "activación"
        var activacionItems = allCatalog
            .Where(e => e.Name.Contains("activación", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(4, activacionItems.Count);
        Assert.Contains(activacionItems, e => e.Name.Contains("Puente de glúteo", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(activacionItems, e => e.Name.Contains("monster walk", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(activacionItems, e => e.Name.Contains("Face pull", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(activacionItems, e => e.Name.Contains("Plancha", StringComparison.OrdinalIgnoreCase));
        Assert.All(activacionItems, e => Assert.NotEqual(ExerciseType.Weight, e.ExerciseType));

        // ACT & ASSERT: Búsqueda por término "entrada en calor"
        var calorItems = allCatalog
            .Where(e => e.Name.Contains("entrada en calor", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(3, calorItems.Count);
        Assert.Contains(calorItems, e => e.Name.Contains("Saltos a la soga", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(calorItems, e => e.Name.Contains("Jumping jacks", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(calorItems, e => e.Name.Contains("Trote suave o bicicleta", StringComparison.OrdinalIgnoreCase));
        Assert.All(calorItems, e => Assert.Equal(ExerciseType.Time, e.ExerciseType));
    }
}
