using Application.DTOs.AthleteRecords;
using Application.DTOs.Memberships;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.UseCases.AthleteRecord.ListAthleteRecords;
using Application.UseCases.AthleteRecord.RegisterAthleteRecord;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

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
    public async Task Registrar_RecordNuevo_GuardaYDevuelveConNombre()
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
    public async Task Registrar_RecordVigente_DesactivaAnteriorYDejaNuevo()
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
    public async Task Listar_RecordsVarios_DevuelveSoloVigentes()
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
    [Trait("Category", "Isolation")]
    public async Task Listar_RecordsOtroPf_NoDevuelveNada()
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
    public async Task Registrar_AtletaOtroPf_LanzaNotFound()
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
    public async Task Registrar_EjercicioInexistente_LanzaNotFound()
    {
        await using var context = CreateContext();

        await Assert.ThrowsAsync<NotFoundException>(() => CreateRegisterHandler(context).Handle(
            new RegisterAthleteRecordCommand(_athleteId, new SaveAthleteRecordRequest(Guid.NewGuid(), 100m, "TestedInSession"))));
    }

    [Theory]
    [InlineData(0, "TestedInSession")]
    [InlineData(100, "Medido")]
    public async Task Registrar_DatosInvalidos_LanzaDomainException(decimal weight, string origin)
    {
        var squat = await SeedExerciseAsync("Sentadilla trasera con barra");
        await using var context = CreateContext();

        await Assert.ThrowsAsync<DomainException>(() => CreateRegisterHandler(context).Handle(
            new RegisterAthleteRecordCommand(_athleteId, new SaveAthleteRecordRequest(squat.Id, weight, origin))));
    }

    [Fact]
    public async Task Listar_TresRecordsSentadilla_DevuelveEvolucion()
    {
        // Criterio de aceptación KAN-434: DADO un atleta con 3 récords de sentadilla CUANDO se pide su evolución ENTONCES devuelve los 3 con su fecha.
        var squat = await SeedExerciseAsync("Sentadilla trasera con barra");

        var date1 = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var date2 = new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc);
        var date3 = new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);

        await using (var context = CreateContext())
        {
            var r1 = AthleteRecord.Create(_athleteId, squat.Id, 100m, RecordSource.Tested, date1);
            r1.Deactivate();
            var r2 = AthleteRecord.Create(_athleteId, squat.Id, 110m, RecordSource.Tested, date2);
            r2.Deactivate();
            var r3 = AthleteRecord.Create(_athleteId, squat.Id, 120m, RecordSource.Tested, date3);

            await context.AthleteRecords.AddRangeAsync(r1, r2, r3);
            await context.SaveChangesAsync();
        }

        await using var listContext = CreateContext();
        var handler = CreateListHandler(listContext);

        // Cuando se pide la evolución del ejercicio sentadilla (exerciseId especificado)
        var evolution = await handler.Handle(new ListAthleteRecordsQuery(_athleteId, ExerciseId: squat.Id));

        Assert.Equal(3, evolution.Count);
        Assert.Equal(100m, evolution[0].WeightOneRepMaxKg);
        Assert.Equal(date1, evolution[0].AchievedAt);
        Assert.Equal(110m, evolution[1].WeightOneRepMaxKg);
        Assert.Equal(date2, evolution[1].AchievedAt);
        Assert.Equal(120m, evolution[2].WeightOneRepMaxKg);
        Assert.Equal(date3, evolution[2].AchievedAt);
    }

    [Fact]
    public async Task Listar_RangoFechas_FiltraPorRango()
    {
        var squat = await SeedExerciseAsync("Sentadilla trasera con barra");

        await using (var context = CreateContext())
        {
            var r1 = AthleteRecord.Create(_athleteId, squat.Id, 100m, RecordSource.Tested, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));
            r1.Deactivate();
            var r2 = AthleteRecord.Create(_athleteId, squat.Id, 110m, RecordSource.Tested, new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc));
            r2.Deactivate();
            var r3 = AthleteRecord.Create(_athleteId, squat.Id, 120m, RecordSource.Tested, new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc));

            await context.AthleteRecords.AddRangeAsync(r1, r2, r3);
            await context.SaveChangesAsync();
        }

        await using var listContext = CreateContext();
        var handler = CreateListHandler(listContext);

        var filtered = await handler.Handle(new ListAthleteRecordsQuery(
            _athleteId,
            ExerciseId: squat.Id,
            From: new DateOnly(2026, 2, 1),
            To: new DateOnly(2026, 4, 1)));

        Assert.Single(filtered);
        Assert.Equal(110m, filtered[0].WeightOneRepMaxKg);
    }
}
