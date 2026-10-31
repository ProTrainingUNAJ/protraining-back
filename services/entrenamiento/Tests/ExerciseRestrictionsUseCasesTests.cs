using Application.Exceptions;
using Application.Interfaces.Services;
using Application.UseCases.Exercise.CheckRestrictions;
using Application.UseCases.Exercise.GetSubstitutes;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Services;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests;

public class ExerciseRestrictionsUseCasesTests
{
    private sealed class TestCurrentUser(Guid? pfId = null) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    private static AppDbContext CreateInMemoryDbContext(string dbName, Guid? pfId = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new AppDbContext(options, new TestCurrentUser(pfId));
    }

    [Fact]
    public async Task CheckRestrictionsHandler_EjecutaCruceCorrectamente()
    {
        var dbName = Guid.NewGuid().ToString();

        var sentadilla = Exercise.Create("Sentadilla libre", "Piernas");
        var prensa = Exercise.Create("Prensa 45", "Piernas");
        var press = Exercise.Create("Press Banca", "Pecho");

        var restriction = ExerciseRestriction.Create("cuadriceps", sentadilla.Id, "Sobrecarga en tendón rotuliano", minPainLevel: 2);
        restriction.AddSubstitute(prensa, 1);

        // Guardamos los ejercicios como catálogo base (sin PF asignado, pfId = null)
        await using (var context = CreateInMemoryDbContext(dbName, pfId: null))
        {
            await context.Exercises.AddRangeAsync(sentadilla, prensa, press);
            await context.ExerciseRestrictions.AddAsync(restriction);
            await context.SaveChangesAsync();
        }

        // Consultamos como un PF o Atleta logueado
        await using (var context = CreateInMemoryDbContext(dbName, pfId: Guid.NewGuid()))
        {
            var repo = new ExerciseRestrictionRepository(context);
            var checker = new ExerciseRestrictionChecker();
            var handler = new CheckExerciseRestrictionsQueryHandler(repo, checker);

            var query = new CheckExerciseRestrictionsQuery
            {
                PainZones = "cuadriceps:2",
                ExerciseIds = [sentadilla.Id, press.Id]
            };

            var results = await handler.Handle(query);

            Assert.Equal(2, results.Count);

            var sentadillaResult = results.First(r => r.ExerciseId == sentadilla.Id);
            Assert.True(sentadillaResult.Restricted);
            Assert.Equal("cuadriceps", sentadillaResult.Zone);
            Assert.Equal(2, sentadillaResult.PainLevel);
            Assert.Single(sentadillaResult.Substitutes);
            Assert.Equal(prensa.Id, sentadillaResult.Substitutes[0].ExerciseId);
            Assert.Equal("Prensa 45", sentadillaResult.Substitutes[0].Name);

            var pressResult = results.First(r => r.ExerciseId == press.Id);
            Assert.False(pressResult.Restricted);
            Assert.Empty(pressResult.Substitutes);
        }
    }

    [Fact]
    public async Task GetSubstitutesHandler_EjercicioValidoYZona_DevuelveSustitutos()
    {
        var dbName = Guid.NewGuid().ToString();

        var sentadilla = Exercise.Create("Sentadilla frontal", "Piernas");
        var sub1 = Exercise.Create("Prensa de piernas", "Piernas");
        var sub2 = Exercise.Create("Sentadillas con bandas", "Piernas");

        var restriction = ExerciseRestriction.Create("cuadriceps", sentadilla.Id, "Torque femororrotuliano", minPainLevel: 1);
        restriction.AddSubstitute(sub2, 2);
        restriction.AddSubstitute(sub1, 1);

        // Guardamos los ejercicios como catálogo base (sin PF asignado, pfId = null)
        await using (var context = CreateInMemoryDbContext(dbName, pfId: null))
        {
            await context.Exercises.AddRangeAsync(sentadilla, sub1, sub2);
            await context.ExerciseRestrictions.AddAsync(restriction);
            await context.SaveChangesAsync();
        }

        // Consultamos como un PF o Atleta logueado
        await using (var context = CreateInMemoryDbContext(dbName, pfId: Guid.NewGuid()))
        {
            var exerciseRepo = new ExerciseRepository(context);
            var restrictionRepo = new ExerciseRestrictionRepository(context);
            var checker = new ExerciseRestrictionChecker();
            var handler = new GetExerciseSubstitutesQueryHandler(exerciseRepo, restrictionRepo, checker);

            var result = await handler.Handle(new GetExerciseSubstitutesQuery
            {
                ExerciseId = sentadilla.Id,
                Zone = "cuadriceps"
            });

            Assert.Equal(2, result.Count);
            Assert.Equal(sub1.Id, result[0].ExerciseId);
            Assert.Equal("Prensa de piernas", result[0].Name);
            Assert.Equal(sub2.Id, result[1].ExerciseId);
            Assert.Equal("Sentadillas con bandas", result[1].Name);
        }
    }

    [Fact]
    public async Task GetSubstitutesHandler_EjercicioNoExiste_LanzaNotFoundException()
    {
        var dbName = Guid.NewGuid().ToString();

        await using var context = CreateInMemoryDbContext(dbName, pfId: Guid.NewGuid());
        var exerciseRepo = new ExerciseRepository(context);
        var restrictionRepo = new ExerciseRestrictionRepository(context);
        var checker = new ExerciseRestrictionChecker();
        var handler = new GetExerciseSubstitutesQueryHandler(exerciseRepo, restrictionRepo, checker);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetExerciseSubstitutesQuery
        {
            ExerciseId = Guid.NewGuid(),
            Zone = "cuadriceps"
        }));

        Assert.Equal("El ejercicio no existe.", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetSubstitutesHandler_ZonaVacia_LanzaDomainException(string? zone)
    {
        var dbName = Guid.NewGuid().ToString();

        await using var context = CreateInMemoryDbContext(dbName, pfId: Guid.NewGuid());
        var exerciseRepo = new ExerciseRepository(context);
        var restrictionRepo = new ExerciseRestrictionRepository(context);
        var checker = new ExerciseRestrictionChecker();
        var handler = new GetExerciseSubstitutesQueryHandler(exerciseRepo, restrictionRepo, checker);

        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new GetExerciseSubstitutesQuery
        {
            ExerciseId = Guid.NewGuid(),
            Zone = zone!
        }));

        Assert.Equal("La zona de dolor es obligatoria.", ex.Message);
    }

    [Fact]
    public async Task GetSubstitutesHandler_ZonaInvalida_LanzaDomainException()
    {
        var dbName = Guid.NewGuid().ToString();

        await using var context = CreateInMemoryDbContext(dbName, pfId: Guid.NewGuid());
        var exerciseRepo = new ExerciseRepository(context);
        var restrictionRepo = new ExerciseRestrictionRepository(context);
        var checker = new ExerciseRestrictionChecker();
        var handler = new GetExerciseSubstitutesQueryHandler(exerciseRepo, restrictionRepo, checker);

        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new GetExerciseSubstitutesQuery
        {
            ExerciseId = Guid.NewGuid(),
            Zone = "zona_inventada"
        }));

        Assert.Equal("La zona 'zona_inventada' no es una zona de dolor válida.", ex.Message);
    }

    [Fact]
    public async Task ExerciseController_Endpoints_RetornanOkConDatos()
    {
        var dbName = Guid.NewGuid().ToString();

        var sentadilla = Exercise.Create("Sentadilla libre", "Piernas");
        var prensa = Exercise.Create("Prensa 45", "Piernas");

        var restriction = ExerciseRestriction.Create("cuadriceps", sentadilla.Id, "Sobrecarga en tendón rotuliano", minPainLevel: 1);
        restriction.AddSubstitute(prensa, 1);

        await using (var context = CreateInMemoryDbContext(dbName, pfId: null))
        {
            await context.Exercises.AddRangeAsync(sentadilla, prensa);
            await context.ExerciseRestrictions.AddAsync(restriction);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateInMemoryDbContext(dbName, pfId: Guid.NewGuid()))
        {
            var exerciseRepo = new ExerciseRepository(context);
            var restrictionRepo = new ExerciseRestrictionRepository(context);
            var checker = new ExerciseRestrictionChecker();

            var listHandler = new Application.UseCases.Exercise.ListExercises.ListExercisesQueryHandler(exerciseRepo);
            var checkHandler = new CheckExerciseRestrictionsQueryHandler(restrictionRepo, checker);
            var getSubstitutesHandler = new GetExerciseSubstitutesQueryHandler(exerciseRepo, restrictionRepo, checker);

            var controller = new API.Controllers.ExerciseController(listHandler, checkHandler, getSubstitutesHandler, null!);

            // Test POST /restrictions/check
            var postResult = await controller.CheckRestrictions(new Application.DTOs.Exercise.CheckExerciseRestrictionsRequest
            {
                PainZones = "cuadriceps:2",
                ExerciseIds = [sentadilla.Id]
            });

            var okPost = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(postResult.Result);
            var listPost = Assert.IsType<List<Application.DTOs.Exercise.ExerciseRestrictionCheckResultDto>>(okPost.Value);
            Assert.Single(listPost);
            Assert.True(listPost[0].Restricted);

            // Test GET /{id}/substitutes?zone=cuadriceps
            var getResult = await controller.GetSubstitutes(sentadilla.Id, "cuadriceps");
            var okGet = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(getResult.Result);
            var listGet = Assert.IsType<List<Application.DTOs.Exercise.ExerciseSubstituteDto>>(okGet.Value);
            Assert.Single(listGet);
            Assert.Equal("Prensa 45", listGet[0].Name);
        }
    }
}
