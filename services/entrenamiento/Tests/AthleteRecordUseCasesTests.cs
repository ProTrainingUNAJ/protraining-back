using Application.DTOs.AthleteRecords;
using Application.DTOs.Memberships;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.UseCases.AthleteRecord.ListAthleteRecords;
using Application.UseCases.AthleteRecord.RegisterAthleteRecord;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Tests;

public class AthleteRecordUseCasesTests
{
    private sealed class TestCurrentUser(Guid? pfId) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _athleteId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Mock<IStructureClient> _structureClient = new();

    public AthleteRecordUseCasesTests()
    {
        // Por defecto el atleta es del PF; los tests que necesitan otro caso lo cambian.
        _structureClient
            .Setup(c => c.GetAthleteByIdAsync(_athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteDto(_athleteId, "Lucas Fernández"));
    }

    private AppDbContext CreateContext(Guid? pfId = null) => CreateContextFor(pfId ?? _pfId);

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

    private RegisterAthleteRecordCommandHandler CreateRegisterHandler(AppDbContext context) =>
        new(new AthleteRecordRepository(context), _structureClient.Object, new UnitOfWork(context));

    private ListAthleteRecordsQueryHandler CreateListHandler(AppDbContext context) =>
        new(new AthleteRecordRepository(context), _structureClient.Object);

    [Fact]
    public async Task Registrar_DadoUnRecordNuevo_DeberiaGuardarloYDevolverloConElNombreDelEjercicio()
    {
        var squat = await SeedExerciseAsync("Sentadilla trasera con barra");

        await using var context = CreateContext();
        var result = await CreateRegisterHandler(context).Handle(
            new RegisterAthleteRecordCommand(_athleteId, new SaveAthleteRecordRequest(squat.Id, 100m, "TestedInSession")));

        Assert.Equal("Sentadilla trasera con barra", result.ExerciseName);
        Assert.Equal(100m, result.WeightOneRepMaxKg);
        Assert.Equal("TestedInSession", result.Origin);
        Assert.True(result.IsCurrent);

        await using var verify = CreateContext();
        var saved = await verify.AthleteRecords.SingleAsync();
        Assert.Equal(RecordSource.Tested, saved.Source);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task Registrar_DadoUnRecordVigente_DeberiaDejarloComoNoVigenteYQuedarSoloElNuevo()
    {
        var squat = await SeedExerciseAsync("Sentadilla trasera con barra");

        await using (var context = CreateContext())
        {
            var handler = CreateRegisterHandler(context);
            await handler.Handle(new RegisterAthleteRecordCommand(_athleteId, new SaveAthleteRecordRequest(squat.Id, 100m, "TestedInSession")));
            await handler.Handle(new RegisterAthleteRecordCommand(_athleteId, new SaveAthleteRecordRequest(squat.Id, 105m, "Reported")));
        }

        await using var verify = CreateContext();
        var records = await verify.AthleteRecords.ToListAsync();
        Assert.Equal(2, records.Count);
        var current = Assert.Single(records, r => r.IsActive);
        Assert.Equal(105m, current.Weight);
        Assert.Equal(RecordSource.Reported, current.Source);
    }

    [Fact]
    public async Task Listar_DadoRecordsVigentesYAnteriores_DeberiaDevolverSoloLosVigentesConSuNombre()
    {
        var squat = await SeedExerciseAsync("Sentadilla trasera con barra");
        var bench = await SeedExerciseAsync("Press de banca plano");

        await using (var context = CreateContext())
        {
            var handler = CreateRegisterHandler(context);
            await handler.Handle(new RegisterAthleteRecordCommand(_athleteId, new SaveAthleteRecordRequest(squat.Id, 100m, "TestedInSession")));
            await handler.Handle(new RegisterAthleteRecordCommand(_athleteId, new SaveAthleteRecordRequest(squat.Id, 105m, "TestedInSession")));
            await handler.Handle(new RegisterAthleteRecordCommand(_athleteId, new SaveAthleteRecordRequest(bench.Id, 80m, "Reported")));
        }

        await using var listContext = CreateContext();
        var current = await CreateListHandler(listContext).Handle(new ListAthleteRecordsQuery(_athleteId));
        var all = await CreateListHandler(listContext).Handle(new ListAthleteRecordsQuery(_athleteId, CurrentOnly: false));

        Assert.Equal(2, current.Count);
        Assert.Equal("Press de banca plano", current[0].ExerciseName);
        Assert.Equal(80m, current[0].WeightOneRepMaxKg);
        Assert.Equal("Sentadilla trasera con barra", current[1].ExerciseName);
        Assert.Equal(105m, current[1].WeightOneRepMaxKg);
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task Listar_DadoRecordsDeOtroPf_NoDeberiaDevolverlos()
    {
        var squat = await SeedExerciseAsync("Sentadilla trasera con barra");

        // Otro PF carga un récord con el mismo id de atleta.
        await using (var otherPfContext = CreateContext(Guid.NewGuid()))
        {
            await CreateRegisterHandler(otherPfContext).Handle(
                new RegisterAthleteRecordCommand(_athleteId, new SaveAthleteRecordRequest(squat.Id, 150m, "TestedInSession")));
        }

        await using var context = CreateContext();
        var result = await CreateListHandler(context).Handle(new ListAthleteRecordsQuery(_athleteId));

        Assert.Empty(result);
    }

    [Fact]
    public async Task Registrar_DadoUnAtletaQueNoEsDelPf_DeberiaLanzarNotFound()
    {
        var squat = await SeedExerciseAsync("Sentadilla trasera con barra");
        var foreignAthleteId = Guid.NewGuid();
        _structureClient
            .Setup(c => c.GetAthleteByIdAsync(foreignAthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StructureAthleteDto?)null);

        await using var context = CreateContext();

        await Assert.ThrowsAsync<NotFoundException>(() => CreateRegisterHandler(context).Handle(
            new RegisterAthleteRecordCommand(foreignAthleteId, new SaveAthleteRecordRequest(squat.Id, 100m, "TestedInSession"))));
        Assert.Empty(await context.AthleteRecords.ToListAsync());
    }

    [Fact]
    public async Task Registrar_DadoUnEjercicioInexistente_DeberiaLanzarNotFound()
    {
        await using var context = CreateContext();

        await Assert.ThrowsAsync<NotFoundException>(() => CreateRegisterHandler(context).Handle(
            new RegisterAthleteRecordCommand(_athleteId, new SaveAthleteRecordRequest(Guid.NewGuid(), 100m, "TestedInSession"))));
    }

    [Theory]
    [InlineData(0, "TestedInSession")]
    [InlineData(100, "Medido")]
    public async Task Registrar_DadoPesoUOrigenInvalidos_DeberiaLanzarDomainException(decimal weight, string origin)
    {
        var squat = await SeedExerciseAsync("Sentadilla trasera con barra");
        await using var context = CreateContext();

        await Assert.ThrowsAsync<DomainException>(() => CreateRegisterHandler(context).Handle(
            new RegisterAthleteRecordCommand(_athleteId, new SaveAthleteRecordRequest(squat.Id, weight, origin))));
    }
}
