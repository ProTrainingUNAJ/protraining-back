using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests;

public class ExerciseRestrictionTests
{
    private sealed class TestCurrentUser(Guid? pfId = null) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    private static AppDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new AppDbContext(options, new TestCurrentUser(Guid.NewGuid()));
    }

    [Fact]
    public void AddSubstitute_MismoEjercicio_LanzaExcepcion()
    {
        // Arrange
        var exerciseId = Guid.NewGuid();
        var restriction = ExerciseRestriction.Create("cuadriceps", exerciseId, "Carga alta sobre la rodilla", minPainLevel: 1);

        // Act & Assert
        var exception = Assert.Throws<DomainException>(() =>
            restriction.AddSubstitute(exerciseId, order: 1)
        );

        Assert.Equal("El ejercicio sustituto no puede ser igual al ejercicio restringido.", exception.Message);
    }

    [Fact]
    public void CreateSubstitute_MismoEjercicio_LanzaExcepcion()
    {
        // Arrange
        var exerciseId = Guid.NewGuid();
        var restriction = ExerciseRestriction.Create("cuadriceps", exerciseId, "Carga alta sobre la rodilla", minPainLevel: 1);

        // Act & Assert
        var exception = Assert.Throws<DomainException>(() =>
            ExerciseSubstitute.Create(restriction, exerciseId, order: 1)
        );

        Assert.Equal("El ejercicio sustituto no puede ser igual al ejercicio restringido.", exception.Message);
    }

    [Fact]
    public async Task GetRestriccion_ZonaCuadriceps_RetornaSustitutosOrdenados()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();

        var restrictedExercise = Exercise.Create("Sentadilla Trasera", "Piernas", "Sentadilla profunda");
        var substitute1 = Exercise.Create("Prensa de Piernas 45", "Piernas", "Menor carga en rodilla");
        var substitute2 = Exercise.Create("Sillón de Cuádriceps", "Piernas", "Aislado");
        var substitute3 = Exercise.Create("Sentadilla Búlgara", "Piernas", "Unilateral");

        var restriction = ExerciseRestriction.Create("cuadriceps", restrictedExercise.Id, "Carga alta sobre la rodilla", minPainLevel: 1);

        // Agregamos sustitutos desordenados para verificar que se puedan ordenar por la propiedad Order
        restriction.AddSubstitute(substitute2.Id, order: 2);
        restriction.AddSubstitute(substitute1.Id, order: 1);
        restriction.AddSubstitute(substitute3.Id, order: 3);

        await using (var context = CreateInMemoryDbContext(dbName))
        {
            await context.Exercises.AddRangeAsync(restrictedExercise, substitute1, substitute2, substitute3);
            await context.ExerciseRestrictions.AddAsync(restriction);
            await context.SaveChangesAsync();
        }

        // Act
        await using (var queryContext = CreateInMemoryDbContext(dbName))
        {
            var result = await queryContext.ExerciseRestrictions
                .Include(r => r.Substitutes)
                .FirstOrDefaultAsync(r => r.Zone == "cuadriceps" && r.ExerciseId == restrictedExercise.Id);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("cuadriceps", result.Zone);
            Assert.Equal(3, result.Substitutes.Count);

            var orderedSubstitutes = result.Substitutes.OrderBy(s => s.Order).ToList();
            Assert.Equal(1, orderedSubstitutes[0].Order);
            Assert.Equal(substitute1.Id, orderedSubstitutes[0].SubstituteExerciseId);

            Assert.Equal(2, orderedSubstitutes[1].Order);
            Assert.Equal(substitute2.Id, orderedSubstitutes[1].SubstituteExerciseId);

            Assert.Equal(3, orderedSubstitutes[2].Order);
            Assert.Equal(substitute3.Id, orderedSubstitutes[2].SubstituteExerciseId);
        }
    }

    [Theory]
    [InlineData("rodilla")]
    [InlineData("hombro")]
    [InlineData("espalda")]
    [InlineData("zona_inventada")]
    public void Create_ZonaInvalida_LanzaExcepcion(string invalidZone)
    {
        var exerciseId = Guid.NewGuid();

        var exception = Assert.Throws<DomainException>(() =>
            ExerciseRestriction.Create(invalidZone, exerciseId, "Motivo válido", minPainLevel: 1)
        );

        Assert.Contains("no es una zona de dolor válida", exception.Message);
    }

    [Theory]
    [InlineData("abdominales")]
    [InlineData("cuadriceps")]
    [InlineData("espalda_baja")]
    [InlineData("cuello_trapecios")]
    [InlineData("isquiotibiales")]
    public void Create_ZonaValida_CreaExitosamente(string validZone)
    {
        var exerciseId = Guid.NewGuid();

        var restriction = ExerciseRestriction.Create(validZone, exerciseId, "Motivo válido", minPainLevel: 1);

        Assert.NotNull(restriction);
        Assert.Equal(validZone, restriction.Zone);
        Assert.Equal(1, restriction.MinPainLevel);
    }

    [Fact]
    public void Create_MinPainLevelMenorAUno_LanzaExcepcion()
    {
        var exerciseId = Guid.NewGuid();

        Assert.Throws<DomainException>(() =>
            ExerciseRestriction.Create("cuadriceps", exerciseId, "Motivo válido", minPainLevel: 0)
        );
    }

    [Fact]
    public void AddSubstitute_OrdenInvalido_LanzaExcepcion()
    {
        var restriction = ExerciseRestriction.Create("cuadriceps", Guid.NewGuid(), "Motivo válido", minPainLevel: 1);

        Assert.Throws<DomainException>(() =>
            restriction.AddSubstitute(Guid.NewGuid(), order: 0)
        );
    }

    [Fact]
    public void AddSubstitute_Duplicado_LanzaExcepcion()
    {
        var restriction = ExerciseRestriction.Create("cuadriceps", Guid.NewGuid(), "Motivo válido", minPainLevel: 1);
        var substituteId = Guid.NewGuid();

        restriction.AddSubstitute(substituteId, order: 1);

        Assert.Throws<DomainException>(() =>
            restriction.AddSubstitute(substituteId, order: 2)
        );
    }
}
