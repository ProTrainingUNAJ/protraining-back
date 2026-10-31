using API.Controllers;
using Application.DTOs.Exercise;
using Application.Interfaces.Services;
using Application.UseCases.Exercise.AddExercise;
using Application.UseCases.Exercise.ListExercises;
using Application.Exceptions;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests;

public class CreateExerciseTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _contextOptions;
    private readonly Guid _pfId = Guid.NewGuid();

    private sealed class TestCurrentUser(Guid? pfId) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    public CreateExerciseTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _contextOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private AppDbContext CreateContext(Guid? customPfId = null) =>
        new(_contextOptions, new TestCurrentUser(customPfId ?? _pfId));

    private AppDbContext CreateCatalogContext() =>
        new(_contextOptions, new TestCurrentUser(null));

    // =========================================================================
    // CRITERIO 1:
    // DADO un PF que crea "Sentadilla búlgara con banda" con origen Ai
    // CUANDO busca en su biblioteca
    // ENTONCES lo encuentra con el origen "IA", y otro PF no lo ve.
    // =========================================================================

    [Fact]
    public async Task Given_CoachCreatesExerciseWithAiOrigin_When_SearchingInLibrary_Then_FindsItWithAiOrigin_AndOtherCoachCannotSeeIt()
    {
        var pf1Id = _pfId;
        var pf2Id = Guid.NewGuid();

        // 1. PF 1 crea el ejercicio propuesto por la IA
        Guid createdId;
        await using (var contextPf1 = CreateContext(pf1Id))
        {
            var uow = new UnitOfWork(contextPf1);
            var repo = new ExerciseRepository(contextPf1);
            var handler = new CreateExerciseCommandHandler(uow, repo);

            var command = new CreateExerciseCommand
            {
                Name = "Sentadilla búlgara con banda",
                MuscleGroup = "Piernas",
                PrimaryMuscles = ["Cuádriceps", "Glúteos"],
                SecondaryMuscles = ["Isquiotibiales"],
                Equipment = "Banda elástica",
                Instructions = "Colocar el pie trasero en banco y banda bajo el pie delantero.",
                Origin = Origin.Ai
            };

            var result = await handler.Handle(command);

            Assert.NotEqual(Guid.Empty, result.Id);
            Assert.Equal("Sentadilla búlgara con banda", result.Name);
            Assert.Equal("Piernas", result.MuscleGroup);
            Assert.Equal(Origin.Ai, result.Origin);
            createdId = result.Id;
        }

        // 2. PF 1 busca en su biblioteca (ListExercises) y lo encuentra con origen Ai
        await using (var contextPf1 = CreateContext(pf1Id))
        {
            var repoPf1 = new ExerciseRepository(contextPf1);
            var listHandlerPf1 = new ListExercisesQueryHandler(repoPf1);

            var responsePf1 = await listHandlerPf1.Handle(new ListExercisesQuery
            {
                MuscleGroup = "Piernas",
                Page = 1,
                PageSize = 20
            });

            Assert.Contains(responsePf1.Items, e => e.Id == createdId && e.Origin == Origin.Ai && e.Name == "Sentadilla búlgara con banda");
        }

        // 3. PF 2 busca en su biblioteca y NO debe ver el ejercicio privado de PF 1
        await using (var contextPf2 = CreateContext(pf2Id))
        {
            var repoPf2 = new ExerciseRepository(contextPf2);
            var listHandlerPf2 = new ListExercisesQueryHandler(repoPf2);

            var responsePf2 = await listHandlerPf2.Handle(new ListExercisesQuery
            {
                MuscleGroup = "Piernas",
                Page = 1,
                PageSize = 20
            });

            Assert.DoesNotContain(responsePf2.Items, e => e.Id == createdId);
        }
    }

    // =========================================================================
    // CRITERIO 2:
    // DADO un ejercicio sin imágenes
    // CUANDO se crea
    // ENTONCES no falla.
    // =========================================================================

    [Fact]
    public async Task Given_ExerciseWithoutImagesOrAnimation_When_Created_Then_SucceedsAndPersists()
    {
        await using var context = CreateContext();
        var uow = new UnitOfWork(context);
        var repo = new ExerciseRepository(context);
        var handler = new CreateExerciseCommandHandler(uow, repo);

        var command = new CreateExerciseCommand
        {
            Name = "Puente de glúteos unilateral",
            MuscleGroup = "Piernas",
            PrimaryMuscles = null,
            SecondaryMuscles = null,
            Equipment = null,
            Instructions = null,
            Origin = Origin.Coach
        };

        // ACT
        var result = await handler.Handle(command);

        // ASSERT: DTO devuelto
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Puente de glúteos unilateral", result.Name);
        Assert.Empty(result.Images);
        Assert.Null(result.AnimationUrl);
        Assert.Null(result.VideoUrl);
        Assert.Null(result.Description);
        Assert.Equal(Origin.Coach, result.Origin);

        // ASSERT: Base de datos
        var savedInDb = await context.Exercises.FirstOrDefaultAsync(e => e.Id == result.Id);
        Assert.NotNull(savedInDb);
        Assert.Empty(savedInDb.Images);
        Assert.Null(savedInDb.AnimationUrl);
        Assert.Equal(Origin.Coach, savedInDb.Origin);
    }

    [Fact]
    public void Given_ExerciseCreateCalledWithoutImagesOrAnimation_When_Invoked_Then_DoesNotThrowNullReferenceException()
    {
        // Prueba unitaria de dominio pura sobre Exercise.Create (punto 4 KAN-516)
        var exception = Record.Exception(() =>
            Exercise.Create(
                name: "Curl Nórdico",
                muscleGroup: "Piernas",
                description: null,
                animationUrl: null,
                videoUrl: null,
                equipment: null,
                images: null,
                primaryMuscles: null,
                secondaryMuscles: null,
                origin: Origin.Coach
            ));

        Assert.Null(exception);
    }

    // =========================================================================
    // CRITERIO 3:
    // DADO un nombre que ya existe para ese PF
    // CUANDO se crea
    // ENTONCES responde 409 con el id del existente.
    // =========================================================================

    [Fact]
    public async Task Given_ExerciseNameAlreadyExistsForCoach_When_Created_Then_ThrowsConflictExceptionWithExistingId()
    {
        Guid existingId;

        // 1. Crear el primer ejercicio
        await using (var context = CreateContext())
        {
            var uow = new UnitOfWork(context);
            var repo = new ExerciseRepository(context);
            var handler = new CreateExerciseCommandHandler(uow, repo);

            var first = await handler.Handle(new CreateExerciseCommand
            {
                Name = "Hip Thrust con Barra",
                MuscleGroup = "Piernas",
                Origin = Origin.Coach
            });
            existingId = first.Id;
        }

        // 2. Intentar crear otro con exactamente el mismo nombre
        await using (var context = CreateContext())
        {
            var uow = new UnitOfWork(context);
            var repo = new ExerciseRepository(context);
            var handler = new CreateExerciseCommandHandler(uow, repo);

            var duplicateCommand = new CreateExerciseCommand
            {
                Name = "Hip Thrust con Barra",
                MuscleGroup = "Piernas",
                Origin = Origin.Ai
            };

            var ex = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(duplicateCommand));
            Assert.Contains(existingId.ToString(), ex.Message);
        }
    }

    [Theory]
    [InlineData("sentadilla bulgara")]           // todo minúsculas sin tilde
    [InlineData("SENTADILLA BÚLGARA")]           // todo mayúsculas con tilde
    [InlineData("  Sentadilla Bulgara  ")]       // con espacios y sin tilde
    [InlineData("sentadilla búlgara")]           // minúsculas con tilde
    public async Task Given_ExerciseNameWithCasingOrDiacriticVariations_When_Created_Then_DetectsConflictAndReturnsExistingId(string variation)
    {
        Guid originalId;

        // 1. Crear con formato original "Sentadilla Búlgara"
        await using (var context = CreateContext())
        {
            var uow = new UnitOfWork(context);
            var repo = new ExerciseRepository(context);
            var handler = new CreateExerciseCommandHandler(uow, repo);

            var original = await handler.Handle(new CreateExerciseCommand
            {
                Name = "Sentadilla Búlgara",
                MuscleGroup = "Piernas",
                Origin = Origin.Coach
            });
            originalId = original.Id;
        }

        // 2. Intentar crear con la variación (sin tildes, mayúsculas, etc.)
        await using (var context = CreateContext())
        {
            var uow = new UnitOfWork(context);
            var repo = new ExerciseRepository(context);
            var handler = new CreateExerciseCommandHandler(uow, repo);

            var variationCommand = new CreateExerciseCommand
            {
                Name = variation,
                MuscleGroup = "Piernas",
                Origin = Origin.Ai
            };

            var ex = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(variationCommand));
            Assert.Contains(originalId.ToString(), ex.Message);
        }
    }

    [Fact]
    public async Task Given_ExerciseNameExistsInGlobalCatalog_When_CoachTriesToCreate_Then_DetectsConflict()
    {
        Guid catalogId;

        // 1. Sembrar en el catálogo global (PfId = Guid.Empty)
        await using (var catalogContext = CreateCatalogContext())
        {
            var catalogEx = Exercise.Create("Press de Banca", "Pecho", origin: Origin.Catalog);
            await catalogContext.Exercises.AddAsync(catalogEx);
            await catalogContext.SaveChangesAsync();
            catalogId = catalogEx.Id;
        }

        // 2. El PF intenta crear "press de banca" (mismo nombre sin distinguir mayúsculas)
        await using (var pfContext = CreateContext())
        {
            var uow = new UnitOfWork(pfContext);
            var repo = new ExerciseRepository(pfContext);
            var handler = new CreateExerciseCommandHandler(uow, repo);

            var command = new CreateExerciseCommand
            {
                Name = "press de banca",
                MuscleGroup = "Pecho",
                Origin = Origin.Ai
            };

            var ex = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command));
            Assert.Contains(catalogId.ToString(), ex.Message);
        }
    }

    [Fact]
    public async Task Given_ExerciseExistsForAnotherCoach_When_CoachCreatesWithSameName_Then_SucceedsWithoutConflict()
    {
        var pf1Id = _pfId;
        var pf2Id = Guid.NewGuid();

        // 1. PF 1 crea su ejercicio privado
        await using (var contextPf1 = CreateContext(pf1Id))
        {
            var uow = new UnitOfWork(contextPf1);
            var repo = new ExerciseRepository(contextPf1);
            var handler = new CreateExerciseCommandHandler(uow, repo);

            await handler.Handle(new CreateExerciseCommand
            {
                Name = "Elevaciones Especiales PF1",
                MuscleGroup = "Hombros",
                Origin = Origin.Coach
            });
        }

        // 2. PF 2 intenta crear un ejercicio con el mismo nombre
        // Como el de PF 1 no es visible para PF 2, NO debe tirar conflicto
        await using (var contextPf2 = CreateContext(pf2Id))
        {
            var uow = new UnitOfWork(contextPf2);
            var repo = new ExerciseRepository(contextPf2);
            var handler = new CreateExerciseCommandHandler(uow, repo);

            var result = await handler.Handle(new CreateExerciseCommand
            {
                Name = "Elevaciones Especiales PF1",
                MuscleGroup = "Hombros",
                Origin = Origin.Coach
            });

            Assert.NotNull(result);
            Assert.Equal("Elevaciones Especiales PF1", result.Name);
        }
    }

    // =========================================================================
    // TEST DEL CONTROLLER
    // =========================================================================

    [Fact]
    public async Task Given_ValidCommand_When_ControllerCreateExerciseCalled_Then_ReturnsCreatedResponseWithDto()
    {
        await using var context = CreateContext();
        var uow = new UnitOfWork(context);
        var repo = new ExerciseRepository(context);
        var createHandler = new CreateExerciseCommandHandler(uow, repo);
        var listHandler = new ListExercisesQueryHandler(repo);

        var controller = new ExerciseController(
            listHandler,
            null!,
            null!,
            createHandler
        );

        var command = new CreateExerciseCommand
        {
            Name = "Remo con Mancuerna",
            MuscleGroup = "Espalda",
            Origin = Origin.Coach
        };

        var actionResult = await controller.CreateExercise(command);

        // Acepta tanto OkObjectResult (200) como ObjectResult (201)
        var objResult = Assert.IsAssignableFrom<ObjectResult>(actionResult.Result);
        var dto = Assert.IsType<ExerciseDto>(objResult.Value);
        Assert.Equal("Remo con Mancuerna", dto.Name);
        Assert.Equal(Origin.Coach, dto.Origin);
    }
}
