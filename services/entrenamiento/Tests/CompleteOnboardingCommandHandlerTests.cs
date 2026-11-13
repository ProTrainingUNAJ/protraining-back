using Application.DTOs.AthleteRecords;
using Application.DTOs.Memberships;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.UseCases.AthleteRecord.CompleteOnboarding;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Tests;

public class CompleteOnboardingCommandHandlerTests
{
    private sealed class TestCurrentUser(Guid? pfId) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _athleteId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Mock<IStructureClient> _structureClient = new();

    public CompleteOnboardingCommandHandlerTests()
    {
        _structureClient
            .Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteMeDto
            {
                AthleteId = _athleteId,
                FirstName = "Lucas",
                LastName = "Gómez",
                FullName = "Lucas Gómez",
                Active = true,
                Categories = []
            });
    }

    private AppDbContext CreateContext() => CreateContextFor(_pfId);

    private AppDbContext CreateContextFor(Guid? pfId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        return new AppDbContext(options, new TestCurrentUser(pfId));
    }

    // Los ejercicios del catálogo no tienen PF (como el seed real): los ven todos los PFs.
    private async Task<Exercise> SeedExerciseAsync(string name)
    {
        var exercise = Exercise.Create(name, "Piernas");
        await using var context = CreateContextFor(null);
        await context.Exercises.AddAsync(exercise);
        await context.SaveChangesAsync();
        return exercise;
    }

    private CompleteOnboardingCommandHandler CreateHandler(AppDbContext context) =>
        new(new AthleteRecordRepository(context), _structureClient.Object, new UnitOfWork(context));

    private static CompleteOnboardingCommand CreateCommand(params (Guid ExerciseId, decimal Weight)[] marks) =>
        new(marks
            .Select(mark => new OnboardingRecordRequest(mark.ExerciseId, mark.Weight))
            .ToList());

    [Fact]
    public async Task Onboarding_DadoPesoDeclarado_DeberiaCrearRecordEstimated()
    {
        var squat = await SeedExerciseAsync("Sentadilla trasera con barra");

        await using var context = CreateContext();
        var result = await CreateHandler(context).Handle(CreateCommand((squat.Id, 70m)));

        var created = Assert.Single(result.Created);
        Assert.Equal(70m, created.WeightOneRepMaxKg);
        Assert.Equal("EstimatedEpley", created.Origin);
        Assert.True(created.IsCurrent);

        await using var verify = CreateContext();
        var saved = await verify.AthleteRecords.SingleAsync();
        Assert.Equal(RecordSource.Estimated, saved.Source);
        Assert.Equal(70m, saved.Weight);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task Onboarding_DadoRecordTestedVigente_DeberiaMantenerloYNoCrearOtro()
    {
        var squat = await SeedExerciseAsync("Sentadilla trasera con barra");
        var existingId = await SeedRecordAsync(squat.Id, 100m, RecordSource.Tested);

        await using (var context = CreateContext())
        {
            var result = await CreateHandler(context).Handle(CreateCommand((squat.Id, 70m)));

            Assert.Empty(result.Created);
        }

        await using var verify = CreateContext();
        var saved = await verify.AthleteRecords.SingleAsync();
        Assert.Equal(existingId, saved.Id);
        Assert.Equal(100m, saved.Weight);
        Assert.Equal(RecordSource.Tested, saved.Source);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task Onboarding_DadoVariosEjerciciosConUnoYaRegistrado_DeberiaCrearSoloElQueFalta()
    {
        var squat = await SeedExerciseAsync("Sentadilla trasera con barra");
        var bench = await SeedExerciseAsync("Press de banca plano");
        await SeedRecordAsync(squat.Id, 100m, RecordSource.Tested);

        await using (var context = CreateContext())
        {
            var result = await CreateHandler(context)
                .Handle(CreateCommand((squat.Id, 70m), (bench.Id, 60m)));

            var created = Assert.Single(result.Created);
            Assert.Equal(bench.Id, created.ExerciseId);
            Assert.Equal(60m, created.WeightOneRepMaxKg);
            Assert.Equal("EstimatedEpley", created.Origin);
        }

        await using var verify = CreateContext();
        var records = await verify.AthleteRecords.ToListAsync();
        Assert.Equal(2, records.Count);

        var squatRecord = Assert.Single(records, record => record.ExerciseId == squat.Id);
        Assert.Equal(100m, squatRecord.Weight);
        Assert.Equal(RecordSource.Tested, squatRecord.Source);
        Assert.True(squatRecord.IsActive);

        var benchRecord = Assert.Single(records, record => record.ExerciseId == bench.Id);
        Assert.Equal(60m, benchRecord.Weight);
        Assert.Equal(RecordSource.Estimated, benchRecord.Source);
        Assert.True(benchRecord.IsActive);
    }

    [Fact]
    public async Task Onboarding_DadoMismoRequestDosVeces_DeberiaCrearUnaSolaMarca()
    {
        var squat = await SeedExerciseAsync("Sentadilla trasera con barra");

        await using (var context = CreateContext())
        {
            var first = await CreateHandler(context).Handle(CreateCommand((squat.Id, 70m)));
            Assert.Single(first.Created);
        }

        await using (var context = CreateContext())
        {
            var second = await CreateHandler(context).Handle(CreateCommand((squat.Id, 90m)));
            Assert.Empty(second.Created);
        }

        await using var verify = CreateContext();
        var saved = await verify.AthleteRecords.SingleAsync();
        Assert.Equal(70m, saved.Weight);
        Assert.Equal(RecordSource.Estimated, saved.Source);
        Assert.True(saved.IsActive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(501)]
    [InlineData(5000)]
    public async Task Onboarding_DadoPesoFueraDeRango_DeberiaLanzarDomainException(decimal weight)
    {
        var squat = await SeedExerciseAsync("Sentadilla trasera con barra");

        await using var context = CreateContext();

        await Assert.ThrowsAsync<DomainException>(() =>
            CreateHandler(context).Handle(CreateCommand((squat.Id, weight))));

        await using var verify = CreateContext();
        Assert.Empty(await verify.AthleteRecords.ToListAsync());
    }

    [Fact]
    public async Task Onboarding_DadoEjercicioRepetido_DeberiaLanzarDomainException()
    {
        var squat = await SeedExerciseAsync("Sentadilla trasera con barra");

        await using var context = CreateContext();

        await Assert.ThrowsAsync<DomainException>(() =>
            CreateHandler(context).Handle(CreateCommand((squat.Id, 70m), (squat.Id, 80m))));

        await using var verify = CreateContext();
        Assert.Empty(await verify.AthleteRecords.ToListAsync());
    }

    [Fact]
    public async Task Onboarding_DadoListaVacia_DeberiaLanzarDomainException()
    {
        await using var context = CreateContext();

        await Assert.ThrowsAsync<DomainException>(() =>
            CreateHandler(context).Handle(CreateCommand()));
    }

    [Fact]
    public async Task Onboarding_DadoEjercicioInexistente_DeberiaLanzarNotFound()
    {
        await using var context = CreateContext();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateHandler(context).Handle(CreateCommand((Guid.NewGuid(), 70m))));
    }

    private async Task<Guid> SeedRecordAsync(Guid exerciseId, decimal weight, RecordSource source)
    {
        var record = AthleteRecord.Create(_athleteId, exerciseId, weight, source, DateTime.UtcNow.AddDays(-1));
        await using var context = CreateContext();
        await context.AthleteRecords.AddAsync(record);
        await context.SaveChangesAsync();
        return record.Id;
    }
}
