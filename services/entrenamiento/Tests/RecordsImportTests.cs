using API.Controllers;
using API.Middleware;
using Application.DTOs.AthleteRecords;
using Application.DTOs.Memberships;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.AthleteRecord.ConfirmRecordsImport;
using Application.UseCases.AthleteRecord.PreviewRecordsImport;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Tests;

public class RecordsImportTests
{
    private sealed class TestCurrentUser(Guid? pfId) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _athleteId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Mock<IStructureClient> _structureClient = new();

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        return new AppDbContext(options, new TestCurrentUser(_pfId));
    }

    private async Task<List<Exercise>> SeedExercisesAsync(params string[] names)
    {
        var exercises = names.Select(name => Exercise.Create(name, "Piernas")).ToList();
        await using var context = CreateContext();
        await context.Exercises.AddRangeAsync(exercises);
        await context.SaveChangesAsync();
        return exercises;
    }

    private PreviewRecordsImportQueryHandler CreatePreviewHandler(AppDbContext context) =>
        new(new ExerciseRepository(context), _structureClient.Object);

    private ConfirmRecordsImportCommandHandler CreateConfirmHandler(AppDbContext context) =>
        new(new AthleteRecordRepository(context), _structureClient.Object, new UnitOfWork(context));

    private void SetupAthletes(params StructureAthleteDto[] athletes) =>
        _structureClient
            .Setup(client => client.ListAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(athletes);

    [Fact]
    public async Task Given_TenRows_When_Previewed_Then_ReturnsEightOkAndTwoAthleteNotFoundWithoutSaving()
    {
        var exercise = Exercise.Create("Sentadilla trasera con barra", "Piernas");
        var repoMock = new Mock<IExerciseRepository>();
        repoMock
            .Setup(repo => repo.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([exercise]);

        var athletes = Enumerable.Range(0, 8)
            .Select(i => new StructureAthleteDto(Guid.NewGuid(), $"Atleta {i}"))
            .ToList();
        SetupAthletes([.. athletes]);

        var csv = "Atleta,Ejercicio,Peso\n"
            + string.Join("\n", athletes.Select((athlete, i) => $"{athlete.FullName},Sentadilla trasera con barra,{100 + i}"))
            + "\nAtleta Fantasma 1,Sentadilla trasera con barra,120"
            + "\nAtleta Fantasma 2,Sentadilla trasera con barra,130";

        await using var context = CreateContext();
        var handler = new PreviewRecordsImportQueryHandler(repoMock.Object, _structureClient.Object);

        var result = await handler.Handle(new PreviewRecordsImportQuery(csv, "Atleta", "Ejercicio", "Peso", null, null));

        Assert.Equal(10, result.Count);
        Assert.Equal(8, result.Count(row => row.Status == ImportRecordRowStatus.Ok));
        Assert.Equal(2, result.Count(row => row.Status == ImportRecordRowStatus.AthleteNotFound));
        Assert.All(
            result.Where(row => row.Status == ImportRecordRowStatus.Ok),
            row => Assert.Equal("Sentadilla trasera con barra", row.MatchedExerciseName));
        Assert.Empty(await context.AthleteRecords.ToListAsync());

        // El catálogo se resuelve en una sola consulta, sin buscar por cada fila.
        repoMock.Verify(repo => repo.ListAllAsync(It.IsAny<CancellationToken>()), Times.Once);
        repoMock.Verify(repo => repo.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        repoMock.Verify(repo => repo.ListAsync(
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_NonExactExerciseName_When_Previewed_Then_ReturnsExerciseApproximateWithSuggestion()
    {
        await SeedExercisesAsync("Sentadilla trasera con barra", "Press de banca plano");
        var athlete = new StructureAthleteDto(_athleteId, "Lucas Fernández");
        SetupAthletes(athlete);

        var csv = "Atleta,Ejercicio,Peso\nLucas Fernández,Sentadilla,100";

        await using var context = CreateContext();
        var result = await CreatePreviewHandler(context).Handle(
            new PreviewRecordsImportQuery(csv, "Atleta", "Ejercicio", "Peso", null, null));

        var row = Assert.Single(result);
        Assert.Equal(ImportRecordRowStatus.ExerciseApproximate, row.Status);
        Assert.Equal("Sentadilla trasera con barra", row.MatchedExerciseName);
        Assert.Null(row.ExerciseId);
        Assert.Equal("¿Es «Sentadilla trasera con barra»?", row.Message);
        Assert.NotEqual("Listo para importar.", row.Message);
    }

    [Fact]
    public async Task Given_FileLargerThanOneMegabyte_When_Previewed_Then_ThrowsDomainExceptionAndMiddlewareReturnsBadRequest()
    {
        var previewHandler = new PreviewRecordsImportQueryHandler(
            Mock.Of<IExerciseRepository>(),
            Mock.Of<IStructureClient>());
        var confirmHandler = new ConfirmRecordsImportCommandHandler(
            Mock.Of<IAthleteRecordRepository>(),
            Mock.Of<IStructureClient>(),
            Mock.Of<IUnitOfWork>());
        var controller = new RecordsImportController(previewHandler, confirmHandler);

        var content = new byte[1_048_577];
        var file = new FormFile(new MemoryStream(content), 0, content.Length, "file", "records.csv");

        var exception = await Assert.ThrowsAsync<DomainException>(() => controller.Preview(
            file, "Atleta", "Ejercicio", "Peso", null, null, CancellationToken.None));

        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        await using var provider = services.BuildServiceProvider();
        httpContext.RequestServices = provider;
        var middleware = new ExceptionMiddleware(_ => throw exception, NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext);

        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task Given_ExistingActiveRecord_When_ImportConfirmed_Then_DeactivatesItAndLeavesEightCurrent()
    {
        var exercises = await SeedExercisesAsync(
            "Sentadilla trasera con barra",
            "Press de banca plano",
            "Peso muerto convencional",
            "Press militar",
            "Remo con barra",
            "Dominadas",
            "Curl de bíceps",
            "Extensión de tríceps");

        var previousRecords = exercises
            .Select((exercise, i) => AthleteRecord.Create(
                _athleteId, exercise.Id, 50m + i, RecordSource.Reported, DateTime.UtcNow.AddDays(-10)))
            .ToList();

        await using (var seedContext = CreateContext())
        {
            await seedContext.AthleteRecords.AddRangeAsync(previousRecords);
            await seedContext.SaveChangesAsync();
        }

        SetupAthletes(new StructureAthleteDto(_athleteId, "Lucas Fernández"));

        var achievedAt = DateTime.UtcNow.AddDays(-1);
        var rows = exercises
            .Select((exercise, i) => new ConfirmImportRecordRowRequest(
                _athleteId, exercise.Id, 100m + i, achievedAt, "Reported"))
            .ToList();

        await using (var context = CreateContext())
        {
            var created = await CreateConfirmHandler(context).Handle(
                new ConfirmRecordsImportCommand(new ConfirmImportRecordsRequest(rows)));

            Assert.Equal(8, created.Count);
            Assert.All(created, record => Assert.True(record.IsCurrent));
        }

        await using var verifyContext = CreateContext();
        var records = await verifyContext.AthleteRecords.ToListAsync();

        Assert.Equal(16, records.Count);
        Assert.Equal(8, records.Count(record => record.IsActive));
        Assert.Equal(8, records.Count(record => !record.IsActive));

        var activeByExercise = records
            .Where(record => record.IsActive)
            .ToDictionary(record => record.ExerciseId);
        var inactiveByExercise = records
            .Where(record => !record.IsActive)
            .ToDictionary(record => record.ExerciseId);

        for (var i = 0; i < exercises.Count; i++)
        {
            var exerciseId = exercises[i].Id;
            Assert.Equal(100m + i, activeByExercise[exerciseId].Weight);
            Assert.Equal(50m + i, inactiveByExercise[exerciseId].Weight);
        }

        // Queda exactamente un vigente por ejercicio.
        Assert.Equal(8, records.Where(record => record.IsActive).Select(record => record.ExerciseId).Distinct().Count());
    }
}
