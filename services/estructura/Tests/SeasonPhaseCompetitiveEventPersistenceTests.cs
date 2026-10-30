using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Moq;

namespace Estructura.Tests;

public sealed class SeasonPhaseCompetitiveEventPersistenceTests : IAsyncDisposable
{
    private readonly SqliteConnection _sqliteConnection;

    public SeasonPhaseCompetitiveEventPersistenceTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();
    }

    [Fact]
    public void Given_AppDbContextModel_When_InspectingEntityConfigurations_Then_TableNamesIndexesForeignKeysAndConstraintsAreCorrect()
    {
        // arrange
        using var context = CreateDbContextForPf(Guid.NewGuid());
        var model = context.GetService<IDesignTimeModel>().Model;

        // 1. CompetitiveEvent verification
        var eventEntity = model.FindEntityType(typeof(CompetitiveEvent));
        Assert.NotNull(eventEntity);
        Assert.Equal("competitive_events", eventEntity.GetTableName());

        var eventCategoryFk = eventEntity.GetForeignKeys()
            .FirstOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(Category));
        Assert.NotNull(eventCategoryFk);
        Assert.Equal(DeleteBehavior.Restrict, eventCategoryFk.DeleteBehavior);

        var eventIndex = eventEntity.GetIndexes()
            .FirstOrDefault(ix => ix.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(CompetitiveEvent.CategoryId), nameof(CompetitiveEvent.Date) }));
        Assert.NotNull(eventIndex);

        // 2. SeasonPhase verification
        var phaseEntity = model.FindEntityType(typeof(SeasonPhase));
        Assert.NotNull(phaseEntity);
        Assert.Equal("season_phases", phaseEntity.GetTableName());

        var phaseCategoryFk = phaseEntity.GetForeignKeys()
            .FirstOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(Category));
        Assert.NotNull(phaseCategoryFk);
        Assert.Equal(DeleteBehavior.Restrict, phaseCategoryFk.DeleteBehavior);

        var phaseIndex = phaseEntity.GetIndexes()
            .FirstOrDefault(ix => ix.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(SeasonPhase.CategoryId), nameof(SeasonPhase.StartDate) }));
        Assert.NotNull(phaseIndex);

        // 3. Check constraint verification
        var checkConstraints = phaseEntity.GetCheckConstraints();
        var dateConstraint = checkConstraints.FirstOrDefault(c => c.Name == "ck_season_phases_date_range");
        Assert.NotNull(dateConstraint);
        Assert.Equal("end_date >= start_date", dateConstraint.Sql);
    }

    [Fact]
    public async Task Given_AuthenticatedPf_When_SavingEventAndPhase_Then_PfIdAndCreatedAtAreAutomaticallyAssigned()
    {
        // arrange
        var pfId = Guid.NewGuid();
        await using var context = CreateDbContextForPf(pfId);
        await context.Database.EnsureCreatedAsync();

        var org = Organization.Create("Club Central");
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        var category = Category.Create(org.Id, "Categoría Sub 20");
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var ev = CompetitiveEvent.Create(
            category.Id,
            new DateOnly(2026, 6, 20),
            "Club Rivadavia",
            CompetitiveEventVenue.Home,
            CompetitiveEventType.Official,
            new TimeOnly(15, 0),
            "Clásico de la liga");

        var phase = SeasonPhase.Create(
            category.Id,
            "Fase Competitiva",
            SeasonPhaseType.Competitive,
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 11, 30));

        // act
        context.CompetitiveEvents.Add(ev);
        context.SeasonPhases.Add(phase);
        await context.SaveChangesAsync();

        // assert: verificar que fueron asignados los campos base
        await using var verifyContext = CreateDbContextForPf(pfId);
        var persistedEvent = await verifyContext.CompetitiveEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == ev.Id);
        var persistedPhase = await verifyContext.SeasonPhases.AsNoTracking().FirstOrDefaultAsync(p => p.Id == phase.Id);

        Assert.NotNull(persistedEvent);
        Assert.Equal(pfId, persistedEvent.PfId);
        Assert.True(persistedEvent.CreatedAt > DateTime.MinValue);
        Assert.Null(persistedEvent.DeletedAt);

        Assert.NotNull(persistedPhase);
        Assert.Equal(pfId, persistedPhase.PfId);
        Assert.True(persistedPhase.CreatedAt > DateTime.MinValue);
        Assert.Null(persistedPhase.DeletedAt);
    }

    [Fact]
    public async Task Given_DataBelongingToPfA_When_QueriedByPfB_Then_GlobalFilterEnforcesMultitenantIsolation()
    {
        // arrange
        var pfAId = Guid.NewGuid();
        var pfBId = Guid.NewGuid();

        Guid eventId;
        Guid phaseId;

        await using (var contextA = CreateDbContextForPf(pfAId))
        {
            await contextA.Database.EnsureCreatedAsync();

            var org = Organization.Create("Club PF A");
            contextA.Organizations.Add(org);
            await contextA.SaveChangesAsync();

            var category = Category.Create(org.Id, "Categoría PF A");
            contextA.Categories.Add(category);
            await contextA.SaveChangesAsync();

            var ev = CompetitiveEvent.Create(
                category.Id,
                new DateOnly(2026, 7, 10),
                "Rival",
                CompetitiveEventVenue.Away,
                CompetitiveEventType.Friendly);
            var phase = SeasonPhase.Create(
                category.Id,
                "Pretemporada",
                SeasonPhaseType.Preseason,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 31));

            contextA.CompetitiveEvents.Add(ev);
            contextA.SeasonPhases.Add(phase);
            await contextA.SaveChangesAsync();

            eventId = ev.Id;
            phaseId = phase.Id;
        }

        // act & assert: PF B no puede ver ni consultar entidades de PF A
        await using var contextB = CreateDbContextForPf(pfBId);
        var leakedEvent = await contextB.CompetitiveEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId);
        var leakedPhase = await contextB.SeasonPhases.AsNoTracking().FirstOrDefaultAsync(p => p.Id == phaseId);

        Assert.Null(leakedEvent);
        Assert.Null(leakedPhase);
    }

    [Fact]
    public async Task Given_DeletedEventAndPhase_When_Queried_Then_SoftDeleteExcludesThemFromQueries()
    {
        // arrange
        var pfId = Guid.NewGuid();
        await using var context = CreateDbContextForPf(pfId);
        await context.Database.EnsureCreatedAsync();

        var org = Organization.Create("Club");
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        var category = Category.Create(org.Id, "Sub 17");
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var ev = CompetitiveEvent.Create(category.Id, new DateOnly(2026, 8, 1), "Rival", CompetitiveEventVenue.Neutral, CompetitiveEventType.Tournament);
        var phase = SeasonPhase.Create(category.Id, "Transición", SeasonPhaseType.Transition, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15));

        context.CompetitiveEvents.Add(ev);
        context.SeasonPhases.Add(phase);
        await context.SaveChangesAsync();

        // act: eliminar a través de AppDbContext (soft delete)
        context.CompetitiveEvents.Remove(ev);
        context.SeasonPhases.Remove(phase);
        await context.SaveChangesAsync();

        // assert: no aparecen en consultas normales
        await using var verifyContext = CreateDbContextForPf(pfId);
        var deletedEvent = await verifyContext.CompetitiveEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == ev.Id);
        var deletedPhase = await verifyContext.SeasonPhases.AsNoTracking().FirstOrDefaultAsync(p => p.Id == phase.Id);

        Assert.Null(deletedEvent);
        Assert.Null(deletedPhase);

        // assert: existen en base con deleted_at poblado al ignorar el query filter
        var rawEvent = await verifyContext.CompetitiveEvents.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == ev.Id);
        var rawPhase = await verifyContext.SeasonPhases.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == phase.Id);

        Assert.NotNull(rawEvent);
        Assert.NotNull(rawEvent.DeletedAt);
        Assert.NotNull(rawPhase);
        Assert.NotNull(rawPhase.DeletedAt);
    }

    private AppDbContext CreateDbContextForPf(Guid pfId)
    {
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.PfId).Returns(pfId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        return new AppDbContext(options, currentUserMock.Object);
    }

    public async ValueTask DisposeAsync()
    {
        await _sqliteConnection.DisposeAsync();
    }
}
