using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Injuries.CreateAthleteInjury;
using Application.UseCases.Injuries.UpdateInjuryStatus;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Seguimiento.Tests.Integration;

// KAN-419: la lesión queda en la tabla con su PF, en Active y con sus fechas; el filtro por
// pf_id lo aplica el AppDbContext (igual que el resto de Seguimiento) y el alta escribe su
// fila inicial null → Active en el historial clínico.
public sealed class InjuryPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _athleteId = Guid.NewGuid();

    // PF del pedido actual: AppDbContext lo lee en cada operación, así que se puede cambiar
    // entre llamadas para simular dos PFs distintos sobre la misma base.
    private Guid _currentPfId;

    private static readonly DateTime InjuryDate = new(2026, 10, 20);
    private static readonly DateTime EstimatedReturnDate = new(2026, 11, 10);

    public InjuryPersistenceTests()
    {
        _currentPfId = _pfId;
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<IAthleteInjuryRepository, AthleteInjuryRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<CreateAthleteInjuryCommandHandler>();
        services.AddScoped<UpdateInjuryStatusCommandHandler>();

        // Estos tests prueban la persistencia: el atleta siempre es del PF (el 404 se prueba en los de endpoint).
        var structureClient = new Mock<IStructureClient>();
        structureClient
            .Setup(client => client.IsOwnAthleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        services.AddSingleton(structureClient.Object);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(user => user.PfId).Returns(() => _currentPfId);
        services.AddSingleton(currentUser.Object);

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private CreateAthleteInjuryCommandHandler CreateHandler() =>
        _provider.CreateScope().ServiceProvider.GetRequiredService<CreateAthleteInjuryCommandHandler>();

    private UpdateInjuryStatusCommandHandler CreateStatusHandler() =>
        _provider.CreateScope().ServiceProvider.GetRequiredService<UpdateInjuryStatusCommandHandler>();

    private AppDbContext CreateDbContext() =>
        _provider.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    private static CreateAthleteInjuryCommand Command(Guid athleteId) =>
        new(athleteId, "isquiotibiales", "Gradación 3 tras sprint", InjuryDate, EstimatedReturnDate);

    [Fact]
    public async Task Given_ValidInjury_When_Created_Then_PersistsActiveWithDatesAndPf()
    {
        var dto = await CreateHandler().HandleAsync(Command(_athleteId));

        using var db = CreateDbContext();
        var saved = await db.AthleteInjuries.IgnoreQueryFilters().SingleAsync(injury => injury.AthleteId == _athleteId);

        Assert.Equal(InjuryStatus.Active, saved.Status);
        Assert.Equal(InjuryDate, saved.InjuryDate);
        Assert.Equal(EstimatedReturnDate, saved.EstimatedReturnDate);
        Assert.Null(saved.ClearedAt);
        Assert.Equal(_pfId, saved.PfId);
        Assert.Equal(dto.Id, saved.Id);

        // El alta deja exactamente la fila inicial null → Active con el PF del token.
        var initialRow = await db.AthleteInjuryStatusChanges.IgnoreQueryFilters()
            .Where(row => row.InjuryId == saved.Id)
            .SingleAsync();

        Assert.Null(initialRow.FromStatus);
        Assert.Equal(InjuryStatus.Active, initialRow.ToStatus);
        Assert.Equal(_pfId, initialRow.ChangedBy);
        Assert.Equal(_pfId, initialRow.PfId);
        Assert.NotEqual(default, initialRow.ChangedAt);
        Assert.False(string.IsNullOrWhiteSpace(initialRow.Note));
    }

    [Fact]
    public async Task Given_ReturnDateBeforeInjuryDate_When_Created_Then_ThrowsAndDoesNotPersist()
    {
        await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(Command(_athleteId) with
            {
                EstimatedReturnDate = InjuryDate.AddDays(-1)
            }));

        using var db = CreateDbContext();
        Assert.Empty(await db.AthleteInjuries.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.AthleteInjuryStatusChanges.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_InjuriesOfTwoPfs_When_ListedByAthlete_Then_ReturnsOnlyCurrentPfRows()
    {
        var otherPf = Guid.NewGuid();
        var otherAthleteId = Guid.NewGuid();

        await CreateHandler().HandleAsync(Command(_athleteId));

        _currentPfId = otherPf;
        await CreateHandler().HandleAsync(Command(otherAthleteId));
        _currentPfId = _pfId;

        using var db = CreateDbContext();

        // Sin saltar el filtro global: solo las lesiones del PF del token.
        var visible = await db.AthleteInjuries.ToListAsync();
        Assert.Single(visible);
        Assert.Equal(_athleteId, visible[0].AthleteId);

        // Existe la fila del otro PF, pero no es visible desde este token.
        Assert.Equal(2, await db.AthleteInjuries.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Given_InjuriesWithMixedStatus_When_ListedByStatus_Then_ReturnsOnlyThatStatus()
    {
        await CreateHandler().HandleAsync(Command(_athleteId));

        var second = Command(_athleteId) with { Zone = "cuadriceps" };
        await CreateHandler().HandleAsync(second);

        using (var db = CreateDbContext())
        {
            var toUpdate = await db.AthleteInjuries.IgnoreQueryFilters()
                .SingleAsync(injury => injury.Zone == "cuadriceps");
            toUpdate.Status = InjuryStatus.InRecovery;
            await db.SaveChangesAsync();
        }

        using (var db = CreateDbContext())
        {
            var inRecovery = await db.AthleteInjuries
                .Where(injury => injury.AthleteId == _athleteId && injury.Status == InjuryStatus.InRecovery)
                .ToListAsync();

            var active = await db.AthleteInjuries
                .Where(injury => injury.AthleteId == _athleteId && injury.Status == InjuryStatus.Active)
                .ToListAsync();

            Assert.Single(inRecovery);
            Assert.Single(active);
        }
    }

    // KAN-422: salto directo de Activa a Alta médica (CA1). El servicio de cambio de estado
    // lanza DomainException y la tabla queda exactamente igual: sigue en Active, sin
    // cleared_at y sin ninguna fila de transición nueva en el historial clínico.
    [Fact]
    public async Task Given_ActiveInjury_When_SkippingRecoveryToMedicallyCleared_Then_ThrowsAndKeepsDatabaseIntact()
    {
        var injury = await CreateHandler().HandleAsync(Command(_athleteId));

        await Assert.ThrowsAsync<DomainException>(
            () => CreateStatusHandler().HandleAsync(
                new UpdateInjuryStatusCommand(injury.Id, InjuryStatus.MedicallyCleared, "Alta directa sin readaptación.")));

        using var db = CreateDbContext();
        var saved = await db.AthleteInjuries.IgnoreQueryFilters()
            .SingleAsync(row => row.Id == injury.Id);

        Assert.Equal(InjuryStatus.Active, saved.Status);
        Assert.Null(saved.ClearedAt);

        var history = await db.AthleteInjuryStatusChanges.IgnoreQueryFilters()
            .Where(row => row.InjuryId == injury.Id)
            .ToListAsync();

        Assert.Single(history);
        Assert.Null(history[0].FromStatus);
        Assert.Equal(InjuryStatus.Active, history[0].ToStatus);
    }

    // KAN-422: la máquina de estados no permite retroceder. Tras llegar válidamente a
    // En recuperación, el intento de volver a Activa lanza DomainException y la BD no cambia.
    [Fact]
    public async Task Given_InRecoveryInjury_When_TransitioningBackToActive_Then_ThrowsAndKeepsDatabaseInRecovery()
    {
        var injury = await CreateHandler().HandleAsync(Command(_athleteId));
        await CreateStatusHandler().HandleAsync(
            new UpdateInjuryStatusCommand(injury.Id, InjuryStatus.InRecovery, "Inicio de readaptación."));

        await Assert.ThrowsAsync<DomainException>(
            () => CreateStatusHandler().HandleAsync(
                new UpdateInjuryStatusCommand(injury.Id, InjuryStatus.Active, "Recae la lesión.")));

        using var db = CreateDbContext();
        var saved = await db.AthleteInjuries.IgnoreQueryFilters()
            .SingleAsync(row => row.Id == injury.Id);

        Assert.Equal(InjuryStatus.InRecovery, saved.Status);
        Assert.Null(saved.ClearedAt);

        // Solo las dos filas legítimas: el alta (null → Active) y la readaptación.
        var history = await db.AthleteInjuryStatusChanges.IgnoreQueryFilters()
            .Where(row => row.InjuryId == injury.Id)
            .ToListAsync();

        Assert.Equal(2, history.Count);
        Assert.DoesNotContain(history, row => row.FromStatus == InjuryStatus.InRecovery && row.ToStatus == InjuryStatus.Active);
    }

    // KAN-422: repetir el estado actual no es una transición: se rechaza sin tocar la BD.
    [Fact]
    public async Task Given_ActiveInjury_When_TransitioningToSameStatus_Then_ThrowsAndKeepsDatabaseIntact()
    {
        var injury = await CreateHandler().HandleAsync(Command(_athleteId));

        await Assert.ThrowsAsync<DomainException>(
            () => CreateStatusHandler().HandleAsync(
                new UpdateInjuryStatusCommand(injury.Id, InjuryStatus.Active, "Sin cambios.")));

        using var db = CreateDbContext();
        var saved = await db.AthleteInjuries.IgnoreQueryFilters()
            .SingleAsync(row => row.Id == injury.Id);

        Assert.Equal(InjuryStatus.Active, saved.Status);
        Assert.Null(saved.ClearedAt);

        var history = await db.AthleteInjuryStatusChanges.IgnoreQueryFilters()
            .Where(row => row.InjuryId == injury.Id)
            .ToListAsync();

        Assert.Single(history);
    }
}
