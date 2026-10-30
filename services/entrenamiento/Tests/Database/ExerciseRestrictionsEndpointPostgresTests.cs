using API.Controllers;
using Application.DTOs.Exercise;
using Application.Interfaces.Services;
using Application.UseCases.Exercise.CheckRestrictions;
using Application.UseCases.Exercise.GetSubstitutes;
using Application.UseCases.Exercise.ListExercises;
using Domain.Entities;
using Domain.Services;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Testcontainers.PostgreSql;

namespace Entrenamiento.Tests.Database;

public sealed class ExerciseRestrictionsEndpointPostgresTests
{
    private const string PostgresImage = "postgres:16-alpine";

    [Fact]
    [Trait("Category", "RequiresDocker")]
    public async Task Given_RealPostgreSql_When_CheckingRestrictionsForShoulderPain_Then_ReturnsContraindicatedWithSubstitutes()
    {
        await using var postgresContainer = new PostgreSqlBuilder()
            .WithImage(PostgresImage)
            .Build();
        await postgresContainer.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgresContainer.GetConnectionString())
            .Options;

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.PfId).Returns(Guid.NewGuid());

        await using (var seedContext = new AppDbContext(options, currentUserMock.Object))
        {
            // Aplicamos las migraciones reales de EF Core en PostgreSQL
            await seedContext.Database.MigrateAsync();

            // Insertamos ejercicios y su restricción biomecánica
            var pressMilitar = Exercise.Create("Press militar tras nuca", "Hombros");
            var substitute1 = Exercise.Create("Press militar con mancuernas", "Hombros");
            var substitute2 = Exercise.Create("Elevaciones laterales", "Hombros");
            var pressBanca = Exercise.Create("Press de banca", "Pecho");

            var restriction = ExerciseRestriction.Create(
                zone: "hombros",
                exerciseId: pressMilitar.Id,
                reason: "Pinzamiento subacromial y fricción articular",
                minPainLevel: 1);

            restriction.AddSubstitute(substitute1, 1);
            restriction.AddSubstitute(substitute2, 2);

            await seedContext.Exercises.AddRangeAsync(pressMilitar, substitute1, substitute2, pressBanca);
            await seedContext.ExerciseRestrictions.AddAsync(restriction);
            await seedContext.SaveChangesAsync();
        }

        await using (var queryContext = new AppDbContext(options, currentUserMock.Object))
        {
            var exerciseRepo = new ExerciseRepository(queryContext);
            var restrictionRepo = new ExerciseRestrictionRepository(queryContext);
            var checker = new ExerciseRestrictionChecker();

            var listHandler = new ListExercisesQueryHandler(exerciseRepo);
            var checkHandler = new CheckExerciseRestrictionsQueryHandler(restrictionRepo, checker);
            var getSubstitutesHandler = new GetExerciseSubstitutesQueryHandler(exerciseRepo, restrictionRepo, checker);

            var controller = new ExerciseController(listHandler, checkHandler, getSubstitutesHandler);

            // Obtenemos los Ids guardados en PostgreSQL
            var pressMilitar = await queryContext.Exercises.FirstAsync(e => e.Name == "Press militar tras nuca");
            var pressBanca = await queryContext.Exercises.FirstAsync(e => e.Name == "Press de banca");
            var sub1 = await queryContext.Exercises.FirstAsync(e => e.Name == "Press militar con mancuernas");
            var sub2 = await queryContext.Exercises.FirstAsync(e => e.Name == "Elevaciones laterales");

            var request = new CheckExerciseRestrictionsRequest
            {
                PainZones = "hombros:2",
                ExerciseIds = [pressMilitar.Id, pressBanca.Id]
            };

            var actionResult = await controller.CheckRestrictions(request);

            // 3. Assert
            var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
            var results = Assert.IsType<List<ExerciseRestrictionCheckResultDto>>(okResult.Value);

            Assert.Equal(2, results.Count);

            // Press militar tras nuca: contraindicado, con sustitutos en orden
            var restrictedItem = results.First(r => r.ExerciseId == pressMilitar.Id);
            Assert.True(restrictedItem.Restricted);
            Assert.Equal("hombros", restrictedItem.Zone);
            Assert.Equal(2, restrictedItem.PainLevel);
            Assert.Equal("Pinzamiento subacromial y fricción articular", restrictedItem.Reason);
            Assert.Equal(2, restrictedItem.Substitutes.Count);
            Assert.Equal(sub1.Id, restrictedItem.Substitutes[0].ExerciseId);
            Assert.Equal("Press militar con mancuernas", restrictedItem.Substitutes[0].Name);
            Assert.Equal(sub2.Id, restrictedItem.Substitutes[1].ExerciseId);
            Assert.Equal("Elevaciones laterales", restrictedItem.Substitutes[1].Name);

            // Press de banca: no contraindicado
            var nonRestrictedItem = results.First(r => r.ExerciseId == pressBanca.Id);
            Assert.False(nonRestrictedItem.Restricted);
            Assert.Null(nonRestrictedItem.Zone);
            Assert.Null(nonRestrictedItem.PainLevel);
            Assert.Null(nonRestrictedItem.Reason);
            Assert.Empty(nonRestrictedItem.Substitutes);
        }
    }
}
